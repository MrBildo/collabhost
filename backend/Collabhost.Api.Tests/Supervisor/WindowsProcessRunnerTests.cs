using System.Collections.Concurrent;
using System.Runtime.Versioning;

using Collabhost.Api.Shared;
using Collabhost.Api.Supervisor;

using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Supervisor;

[SupportedOSPlatform("windows")]
public class WindowsProcessRunnerTests(ITestOutputHelper output)
{
    private readonly WindowsProcessRunner _runner = new(new XunitLogger(output));

    private static async Task WaitForExitAsync(IProcessHandle handle, int timeoutSeconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (!handle.HasExited && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }
    }

    // The OS process can exit before its OutputDataReceived / ErrorDataReceived
    // events have drained from the pipe-read thread, so a one-shot read after
    // process exit can observe an empty captured collection on a loaded runner.
    // Bounded-poll the captured collection until the expected entry appears or
    // the timeout expires. Class-wide hardening per #315 (umbrella for #198/#264).
    private static async Task WaitForOutputAsync
    (
        ConcurrentBag<string> captured,
        string expectedToken,
        int timeoutSeconds = 10
    )
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            if (captured.Any(line => line.Contains(expectedToken, StringComparison.Ordinal)))
            {
                return;
            }

            await Task.Delay(50);
        }
    }

    private static async Task WaitForOutputAsync<T>
    (
        ConcurrentBag<T> captured,
        Func<T, bool> predicate,
        int timeoutSeconds = 10
    )
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            if (captured.Any(predicate))
            {
                return;
            }

            await Task.Delay(50);
        }
    }

    [Fact]
    [Trait("Platform", "windows")]
    public async Task Start_CapturesStdoutFromProcess()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");

        var captured = new ConcurrentBag<string>();

        var configuration = new ProcessStartConfiguration
        (
            "cmd.exe",
            "/c echo hello-stdout",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            (line, _) => captured.Add(line)
        );

        using var handle = _runner.Start(configuration);

        await WaitForExitAsync(handle);
        await WaitForOutputAsync(captured, "hello-stdout");

        handle.HasExited.ShouldBeTrue();

        captured.ShouldContain(line => line.Contains("hello-stdout", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Platform", "windows")]
    public async Task Start_CapturesStderrFromProcess()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");

        var captured = new ConcurrentBag<(string Line, LogStream Stream)>();

        var configuration = new ProcessStartConfiguration
        (
            "cmd.exe",
            "/c echo hello-stderr 1>&2",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            (line, stream) => captured.Add((line, stream))
        );

        using var handle = _runner.Start(configuration);

        await WaitForExitAsync(handle);
        await WaitForOutputAsync(captured, entry => entry.Line.Contains("hello-stderr", StringComparison.Ordinal));

        handle.HasExited.ShouldBeTrue();

        captured.ShouldContain(entry => entry.Line.Contains("hello-stderr", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Platform", "windows")]
    public async Task Start_ProcessHasPid()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");

        var configuration = new ProcessStartConfiguration
        (
            "cmd.exe",
            "/c echo test",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            (_, _) => { }
        );

        using var handle = _runner.Start(configuration);

        handle.Pid.ShouldBeGreaterThan(0);

        await WaitForExitAsync(handle);
    }

    [Fact]
    [Trait("Platform", "windows")]
    public async Task Start_ExitCodeAvailableAfterExit()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");

        var configuration = new ProcessStartConfiguration
        (
            "cmd.exe",
            "/c exit 42",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            (_, _) => { }
        );

        using var handle = _runner.Start(configuration);

        await WaitForExitAsync(handle);

        handle.HasExited.ShouldBeTrue();
        handle.ExitCode.ShouldBe(42);
    }

    [Fact]
    [Trait("Platform", "windows")]
    public async Task Start_ExitedEventFires()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");

        var exitedCode = -1;
        var exitedFired = new TaskCompletionSource<int>();

        var configuration = new ProcessStartConfiguration
        (
            "cmd.exe",
            "/c exit 7",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            (_, _) => { }
        );

        using var handle = _runner.Start(configuration);

        handle.Exited += code =>
        {
            exitedCode = code;
            exitedFired.TrySetResult(code);
        };

        var completedTask = await Task.WhenAny(exitedFired.Task, Task.Delay(TimeSpan.FromSeconds(10)));

        completedTask.ShouldBe(exitedFired.Task);

        exitedCode.ShouldBe(7);
    }

    [Fact]
    [Trait("Platform", "windows")]
    public async Task TryGracefulShutdown_LongRunningProcess_ReportsUndelivered()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");

        // The honest contract: a running child cannot be gracefully signalled on Windows today.
        // It is started with CREATE_NEW_CONSOLE (its own console), so a host-side
        // GenerateConsoleCtrlEvent -- which only reaches the host's own console -- cannot deliver
        // to it. TryGracefulShutdown must therefore return FALSE for a running process so the
        // supervisor hard-kills immediately instead of waiting out a ShutdownTimeoutSeconds it
        // cannot honor. (The prior assertion here was shutdownSent.ShouldBeTrue() -- it pinned the
        // API-success-means-delivered belief that WAS the bug, and stayed green in the broken state.)
        var configuration = new ProcessStartConfiguration
        (
            "ping",
            "-n 100 127.0.0.1",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            (_, _) => { }
        );

        using var handle = _runner.Start(configuration);

        await Task.Delay(1000);

        handle.HasExited.ShouldBeFalse();

        var shutdownSent = handle.TryGracefulShutdown();

        shutdownSent.ShouldBeFalse();

        // The running process is undisturbed by the (non-)signal -- hard-kill to clean up.
        handle.HasExited.ShouldBeFalse();

        handle.Kill();

        await WaitForExitAsync(handle);
    }

    [Fact]
    [Trait("Platform", "windows")]
    public async Task TryGracefulShutdown_AlreadyExitedProcess_ReturnsTrue()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");

        var configuration = new ProcessStartConfiguration
        (
            "cmd.exe",
            "/c exit 0",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            (_, _) => { }
        );

        using var handle = _runner.Start(configuration);

        await WaitForExitAsync(handle);

        handle.HasExited.ShouldBeTrue();

        var result = handle.TryGracefulShutdown();

        result.ShouldBeTrue();
    }

    [Fact]
    [Trait("Platform", "windows")]
    public async Task Kill_TerminatesRunningProcess()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");

        var configuration = new ProcessStartConfiguration
        (
            "ping",
            "-n 100 127.0.0.1",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            (_, _) => { }
        );

        using var handle = _runner.Start(configuration);

        await Task.Delay(500);

        handle.HasExited.ShouldBeFalse();

        handle.Kill();

        await WaitForExitAsync(handle);

        handle.HasExited.ShouldBeTrue();
    }

    [Fact]
    [Trait("Platform", "windows")]
    public async Task Start_InjectsEnvironmentVariables()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");

        var captured = new ConcurrentBag<string>();

        var environmentVariables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["COLLABHOST_TEST_VAR"] = "graceful-shutdown-test"
        };

        var configuration = new ProcessStartConfiguration
        (
            "cmd.exe",
            "/c echo %COLLABHOST_TEST_VAR%",
            null,
            environmentVariables,
            (line, _) => captured.Add(line)
        );

        using var handle = _runner.Start(configuration);

        await WaitForExitAsync(handle);
        await WaitForOutputAsync(captured, "graceful-shutdown-test");

        handle.HasExited.ShouldBeTrue();

        captured.ShouldContain(line => line.Contains("graceful-shutdown-test", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Platform", "windows")]
    public async Task RunToCompletion_CapturesOutput()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");

        var captured = new ConcurrentBag<string>();

        var configuration = new ProcessStartConfiguration
        (
            "cmd.exe",
            "/c echo run-to-completion-test",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            (line, _) => captured.Add(line)
        );

        var result = await _runner.RunToCompletionAsync(configuration, TimeSpan.FromSeconds(10));

        await WaitForOutputAsync(captured, "run-to-completion-test");

        result.ExitCode.ShouldBe(0);
        result.TimedOut.ShouldBeFalse();

        captured.ShouldContain(line => line.Contains("run-to-completion-test", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Platform", "windows")]
    public async Task RunToCompletion_TimesOut_KillsProcess()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");

        var configuration = new ProcessStartConfiguration
        (
            "ping",
            "-n 100 127.0.0.1",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            (_, _) => { }
        );

        var result = await _runner.RunToCompletionAsync(configuration, TimeSpan.FromSeconds(2));

        result.TimedOut.ShouldBeTrue();
        result.ExitCode.ShouldBe(-1);
    }
}

// No subclasses expected -- test adapter bridging ILogger<T> to xUnit output
file sealed class XunitLogger(ITestOutputHelper output) : ILogger<WindowsProcessRunner>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>
    (
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        output.WriteLine($"[{logLevel}] {formatter(state, exception)}");

        if (exception is not null)
        {
            output.WriteLine(exception.ToString());
        }
    }
}
