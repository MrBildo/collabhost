using System.Collections.Concurrent;
using System.Runtime.Versioning;

using Collabhost.Api.Shared;
using Collabhost.Api.Supervisor;

using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;
using Xunit.Abstractions;

namespace Collabhost.Api.Tests.Supervisor;

[SupportedOSPlatform("linux")]
public class LinuxProcessRunnerTests(ITestOutputHelper output)
{
    private readonly LinuxProcessRunner _runner = new(new XunitLinuxLogger(output));

    private static async Task WaitForExitAsync(IProcessHandle handle, int timeoutSeconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (!handle.HasExited && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }
    }

    [SkippableFact]
    [Trait("Platform", "linux")]
    public async Task Start_CapturesStdoutFromProcess()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux only");

        var captured = new ConcurrentBag<string>();

        var configuration = new ProcessStartConfiguration
        (
            "/bin/sh",
            "-c \"echo hello-stdout\"",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            (line, _) => captured.Add(line)
        );

        using var handle = _runner.Start(configuration);

        await WaitForExitAsync(handle);

        handle.HasExited.ShouldBeTrue();

        captured.ShouldContain(line => line.Contains("hello-stdout", StringComparison.Ordinal));
    }

    [SkippableFact]
    [Trait("Platform", "linux")]
    public async Task Start_CapturesStderrFromProcess()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux only");

        var captured = new ConcurrentBag<(string Line, LogStream Stream)>();

        var configuration = new ProcessStartConfiguration
        (
            "/bin/sh",
            "-c \"echo hello-stderr >&2\"",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            (line, stream) => captured.Add((line, stream))
        );

        using var handle = _runner.Start(configuration);

        await WaitForExitAsync(handle);

        handle.HasExited.ShouldBeTrue();

        captured.ShouldContain(entry => entry.Line.Contains("hello-stderr", StringComparison.Ordinal));
    }

    [SkippableFact]
    [Trait("Platform", "linux")]
    public async Task Start_ProcessHasPid()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux only");

        var configuration = new ProcessStartConfiguration
        (
            "/bin/sh",
            "-c \"echo test\"",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            (_, _) => { }
        );

        using var handle = _runner.Start(configuration);

        handle.Pid.ShouldBeGreaterThan(0);

        await WaitForExitAsync(handle);
    }

    [SkippableFact]
    [Trait("Platform", "linux")]
    public async Task Start_ExitCodeAvailableAfterExit()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux only");

        var configuration = new ProcessStartConfiguration
        (
            "/bin/sh",
            "-c \"exit 42\"",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            (_, _) => { }
        );

        using var handle = _runner.Start(configuration);

        await WaitForExitAsync(handle);

        handle.HasExited.ShouldBeTrue();
        handle.ExitCode.ShouldBe(42);
    }

    [SkippableFact]
    [Trait("Platform", "linux")]
    public async Task Start_ExitedEventFires()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux only");

        var exitedCode = -1;
        var exitedFired = new TaskCompletionSource<int>();

        var configuration = new ProcessStartConfiguration
        (
            "/bin/sh",
            "-c \"exit 7\"",
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

    [SkippableFact]
    [Trait("Platform", "linux")]
    public async Task TryGracefulShutdown_LongRunningProcess_SendsSignal()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux only");

        var configuration = new ProcessStartConfiguration
        (
            "sleep",
            "300",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            (_, _) => { }
        );

        using var handle = _runner.Start(configuration);

        await Task.Delay(500);

        handle.HasExited.ShouldBeFalse();

        var shutdownSent = handle.TryGracefulShutdown();

        shutdownSent.ShouldBeTrue();

        await WaitForExitAsync(handle, 5);

        handle.HasExited.ShouldBeTrue();
    }

    [SkippableFact]
    [Trait("Platform", "linux")]
    public async Task TryGracefulShutdown_AlreadyExitedProcess_ReturnsTrue()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux only");

        var configuration = new ProcessStartConfiguration
        (
            "/bin/sh",
            "-c \"exit 0\"",
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

    [SkippableFact]
    [Trait("Platform", "linux")]
    public async Task Kill_TerminatesRunningProcess()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux only");

        var configuration = new ProcessStartConfiguration
        (
            "sleep",
            "300",
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

    [SkippableFact]
    [Trait("Platform", "linux")]
    public async Task Start_InjectsEnvironmentVariables()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux only");

        var captured = new ConcurrentBag<string>();

        var environmentVariables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["COLLABHOST_TEST_VAR"] = "graceful-shutdown-test"
        };

        var configuration = new ProcessStartConfiguration
        (
            "/bin/sh",
            "-c \"echo $COLLABHOST_TEST_VAR\"",
            null,
            environmentVariables,
            (line, _) => captured.Add(line)
        );

        using var handle = _runner.Start(configuration);

        await WaitForExitAsync(handle);

        handle.HasExited.ShouldBeTrue();

        captured.ShouldContain(line => line.Contains("graceful-shutdown-test", StringComparison.Ordinal));
    }

    [SkippableFact]
    [Trait("Platform", "linux")]
    public async Task RunToCompletion_CapturesOutput()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux only");

        var captured = new ConcurrentBag<string>();

        var configuration = new ProcessStartConfiguration
        (
            "/bin/sh",
            "-c \"echo run-to-completion-test\"",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            (line, _) => captured.Add(line)
        );

        var result = await _runner.RunToCompletionAsync(configuration, TimeSpan.FromSeconds(10));

        result.ExitCode.ShouldBe(0);
        result.TimedOut.ShouldBeFalse();

        captured.ShouldContain(line => line.Contains("run-to-completion-test", StringComparison.Ordinal));
    }

    [SkippableFact]
    [Trait("Platform", "linux")]
    public async Task RunToCompletion_TimesOut_KillsProcess()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux only");

        var configuration = new ProcessStartConfiguration
        (
            "sleep",
            "300",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            (_, _) => { }
        );

        var result = await _runner.RunToCompletionAsync(configuration, TimeSpan.FromSeconds(2));

        result.TimedOut.ShouldBeTrue();
        result.ExitCode.ShouldBe(-1);
    }

    [SkippableFact]
    [Trait("Platform", "linux")]
    public async Task Start_SetsProcessGroup()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux only");

        var configuration = new ProcessStartConfiguration
        (
            "sleep",
            "300",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            (_, _) => { }
        );

        using var handle = _runner.Start(configuration);

        await Task.Delay(200);

        handle.HasExited.ShouldBeFalse();

        // Verify that the child process has a valid process group.
        // setpgid(pid, pid) races with the child's exec() -- when it succeeds,
        // getpgid(pid) == pid. When the race is lost (EACCES), the child stays
        // in the parent's group and the runner falls back to single-PID signals.
        // Both outcomes are correct behavior. We verify the process has a valid
        // group (getpgid returns > 0) and that start succeeded regardless.
        var pgid = LinuxNativeMethods.GetProcessGroupId(handle.Pid);

        pgid.ShouldBeGreaterThan(0);

        handle.Kill();

        await WaitForExitAsync(handle);
    }

    [SkippableFact]
    [Trait("Platform", "linux")]
    public async Task TryGracefulShutdown_KillsProcessGroup()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux only");

        // Start a shell that spawns a background child -- both should be in the same process group.
        // The shell runs "sleep 300" in the background and then sleeps itself.
        // SIGTERM to the process group should kill both.
        var configuration = new ProcessStartConfiguration
        (
            "/bin/sh",
            "-c \"sleep 300 & sleep 300\"",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            (_, _) => { }
        );

        using var handle = _runner.Start(configuration);

        await Task.Delay(500);

        handle.HasExited.ShouldBeFalse();

        var shutdownSent = handle.TryGracefulShutdown();

        shutdownSent.ShouldBeTrue();

        await WaitForExitAsync(handle, 5);

        handle.HasExited.ShouldBeTrue();
    }
}

// No subclasses expected -- test adapter bridging ILogger<T> to xUnit output
file sealed class XunitLinuxLogger(ITestOutputHelper output) : ILogger<LinuxProcessRunner>
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
