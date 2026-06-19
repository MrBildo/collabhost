using System.Collections.Concurrent;
using System.Runtime.Versioning;

using Collabhost.Api.Supervisor;

using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Supervisor;

// Card #330 -- end-to-end proof through the REAL runner spawn path that a hosted
// child does NOT inherit Collabhost's own host-scoped environment. These tests
// poison the current process environment (simulating the systemd unit /
// Windows service setting ASPNETCORE_CONTENTROOT etc. on the supervisor), spawn
// a child via the platform runner, and read what the child actually observes in
// ITS environment. The unit-level ChildProcessEnvironmentTests prove the
// contract; these prove the runners actually route through it.
//
// One class per platform mirrors the sibling LinuxProcessRunnerTests /
// WindowsProcessRunnerTests split so [SupportedOSPlatform] keeps CA1416 quiet.
// Both join EnvironmentPoisoningCollection -- they mutate process-global state.

[SupportedOSPlatform("linux")]
[Collection(nameof(EnvironmentPoisoningCollection))]
public class LinuxProcessRunnerEnvironmentIsolationTests(ITestOutputHelper output)
{
    private readonly LinuxProcessRunner _runner = new(new XunitBridgeLogger<LinuxProcessRunner>(output));

    [Fact]
    [Trait("Platform", "linux")]
    public async Task LinuxRunner_ChildDoesNotInheritCollabhostContentRoot()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux only");

        // Simulate the supervisor running under the production systemd unit.
        using var poison = new EnvironmentPoison
        (
            ("ASPNETCORE_CONTENTROOT", "/opt/collabhost"),
            ("ASPNETCORE_ENVIRONMENT", "Production"),
            ("DOTNET_ENVIRONMENT", "Production"),
            ("COLLABHOST_DATA_PATH", "/var/lib/collabhost/data")
        );

        var captured = new ConcurrentBag<string>();

        // The child echoes the leaked vars. Bracketing makes an empty value
        // distinguishable from "var not set".
        var configuration = new ProcessStartConfiguration
        (
            "/bin/sh",
            "-c \"echo CR=[$ASPNETCORE_CONTENTROOT] ENV=[$ASPNETCORE_ENVIRONMENT] "
                + "DENV=[$DOTNET_ENVIRONMENT] DATA=[$COLLABHOST_DATA_PATH] PATH_SET=[${PATH:+yes}]\"",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            (line, _) => captured.Add(line)
        );

        using var handle = _runner.Start(configuration);

        await TestProcessWait.ForExitAsync(handle);
        await TestProcessWait.ForOutputAsync(captured, "CR=[");

        handle.HasExited.ShouldBeTrue();

        var line = captured.First(l => l.Contains("CR=[", StringComparison.Ordinal));

        // The #330 leak set must be EMPTY in the child even though the supervisor
        // has them set.
        line.ShouldContain("CR=[]");
        line.ShouldContain("ENV=[]");
        line.ShouldContain("DENV=[]");
        line.ShouldContain("DATA=[]");

        // PATH is OS context -- allowlisted -- so the child can still resolve
        // executables.
        line.ShouldContain("PATH_SET=[yes]");
    }

    [Fact]
    [Trait("Platform", "linux")]
    public async Task LinuxRunner_OperatorPinnedViaCapability_IsHonoredInChild()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux only");

        using var poison = new EnvironmentPoison
        (
            ("ASPNETCORE_ENVIRONMENT", "Production")
        );

        var captured = new ConcurrentBag<string>();

        // The supervisor's curated dictionary carries the operator's pin -- it
        // must win in the child, not be stripped as a host var.
        var curated = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Staging"
        };

        var configuration = new ProcessStartConfiguration
        (
            "/bin/sh",
            "-c \"echo ENV=[$ASPNETCORE_ENVIRONMENT]\"",
            null,
            curated,
            (line, _) => captured.Add(line)
        );

        using var handle = _runner.Start(configuration);

        await TestProcessWait.ForExitAsync(handle);
        await TestProcessWait.ForOutputAsync(captured, "ENV=[");

        captured.First(l => l.Contains("ENV=[", StringComparison.Ordinal))
            .ShouldContain("ENV=[Staging]");
    }
}

[SupportedOSPlatform("windows")]
[Collection(nameof(EnvironmentPoisoningCollection))]
public class WindowsProcessRunnerEnvironmentIsolationTests(ITestOutputHelper output)
{
    private readonly WindowsProcessRunner _runner = new(new XunitBridgeLogger<WindowsProcessRunner>(output));

    [Fact]
    [Trait("Platform", "windows")]
    public async Task WindowsRunner_ChildDoesNotInheritCollabhostContentRoot()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");

        using var poison = new EnvironmentPoison
        (
            ("ASPNETCORE_CONTENTROOT", @"C:\Program Files\Collabhost"),
            ("ASPNETCORE_ENVIRONMENT", "Production"),
            ("DOTNET_ENVIRONMENT", "Production"),
            ("COLLABHOST_DATA_PATH", @"C:\ProgramData\Collabhost\data")
        );

        var captured = new ConcurrentBag<string>();

        // cmd.exe leaves [%VAR%] literal when the var is undefined and expands
        // it when defined -- so an absent var shows the literal token.
        var configuration = new ProcessStartConfiguration
        (
            "cmd.exe",
            "/c echo CR=[%ASPNETCORE_CONTENTROOT%] ENV=[%ASPNETCORE_ENVIRONMENT%] "
                + "DENV=[%DOTNET_ENVIRONMENT%] DATA=[%COLLABHOST_DATA_PATH%]",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            (line, _) => captured.Add(line)
        );

        using var handle = _runner.Start(configuration);

        await TestProcessWait.ForExitAsync(handle);
        await TestProcessWait.ForOutputAsync(captured, "CR=[");

        handle.HasExited.ShouldBeTrue();

        var line = captured.First(l => l.Contains("CR=[", StringComparison.Ordinal));

        // Undefined cmd.exe vars stay as the literal %NAME% token -- proof the
        // child never received Collabhost's host values.
        line.ShouldContain("CR=[%ASPNETCORE_CONTENTROOT%]");
        line.ShouldContain("ENV=[%ASPNETCORE_ENVIRONMENT%]");
        line.ShouldContain("DENV=[%DOTNET_ENVIRONMENT%]");
        line.ShouldContain("DATA=[%COLLABHOST_DATA_PATH%]");
    }

    [Fact]
    [Trait("Platform", "windows")]
    public async Task WindowsRunner_OperatorPinnedViaCapability_IsHonoredInChild()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");

        using var poison = new EnvironmentPoison
        (
            ("ASPNETCORE_ENVIRONMENT", "Production")
        );

        var captured = new ConcurrentBag<string>();

        var curated = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Staging"
        };

        var configuration = new ProcessStartConfiguration
        (
            "cmd.exe",
            "/c echo ENV=[%ASPNETCORE_ENVIRONMENT%]",
            null,
            curated,
            (line, _) => captured.Add(line)
        );

        using var handle = _runner.Start(configuration);

        await TestProcessWait.ForExitAsync(handle);
        await TestProcessWait.ForOutputAsync(captured, "ENV=[");

        captured.First(l => l.Contains("ENV=[", StringComparison.Ordinal))
            .ShouldContain("ENV=[Staging]");
    }
}

// Shared poll helpers -- same shape as the sibling runner test files, factored
// out so both platform classes here reuse one copy.
file static class TestProcessWait
{
    public static async Task ForExitAsync(IProcessHandle handle, int timeoutSeconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (!handle.HasExited && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }
    }

    public static async Task ForOutputAsync
    (
        ConcurrentBag<string> captured,
        string token,
        int timeoutSeconds = 10
    )
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            if (captured.Any(line => line.Contains(token, StringComparison.Ordinal)))
            {
                return;
            }

            await Task.Delay(50);
        }
    }
}

// Generic xUnit-output logger bridge -- keeps this file self-contained without
// duplicating two near-identical per-runner adapters.
file sealed class XunitBridgeLogger<T>(ITestOutputHelper output) : ILogger<T>
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
    ) => output.WriteLine($"[{logLevel}] {formatter(state, exception)}");
}
