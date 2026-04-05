using System.Collections.Concurrent;
using System.Runtime.Versioning;

using Collabhost.Api.Shared;
using Collabhost.Api.Supervisor;

using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;
using Xunit.Abstractions;

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

    [Fact]
    public async Task Start_CapturesStdoutFromProcess()
    {
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

        handle.HasExited.ShouldBeTrue();

        captured.ShouldContain(line => line.Contains("hello-stdout", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Start_CapturesStderrFromProcess()
    {
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

        handle.HasExited.ShouldBeTrue();

        captured.ShouldContain(entry => entry.Line.Contains("hello-stderr", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Start_ProcessHasPid()
    {
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
    public async Task Start_ExitCodeAvailableAfterExit()
    {
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
    public async Task Start_ExitedEventFires()
    {
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
    public async Task TryGracefulShutdown_LongRunningProcess_SendsSignal()
    {
        // Verify that CTRL_BREAK_EVENT is successfully sent via GenerateConsoleCtrlEvent.
        // Whether the process actually exits depends on its signal handler -- that is
        // process-specific behavior, not something we control. The supervisor's shutdown
        // flow has a timeout + hard kill fallback for uncooperative processes.
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

        shutdownSent.ShouldBeTrue();

        // Clean up -- kill the process since ping may not honor CTRL_BREAK
        if (!handle.HasExited)
        {
            handle.Kill();

            await WaitForExitAsync(handle);
        }
    }

    [Fact]
    public async Task TryGracefulShutdown_AlreadyExitedProcess_ReturnsTrue()
    {
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
    public async Task Kill_TerminatesRunningProcess()
    {
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
    public async Task Start_InjectsEnvironmentVariables()
    {
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

        handle.HasExited.ShouldBeTrue();

        captured.ShouldContain(line => line.Contains("graceful-shutdown-test", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunToCompletion_CapturesOutput()
    {
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

        result.ExitCode.ShouldBe(0);
        result.TimedOut.ShouldBeFalse();

        captured.ShouldContain(line => line.Contains("run-to-completion-test", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunToCompletion_TimesOut_KillsProcess()
    {
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
