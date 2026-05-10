using System.Diagnostics;
using System.Runtime.Versioning;

using Collabhost.Api.Supervisor;
using Collabhost.Api.Supervisor.Containment;

using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;
using Xunit.Abstractions;

namespace Collabhost.Api.Tests.Supervisor;

[SupportedOSPlatform("linux")]
public class LinuxContainmentTests(ITestOutputHelper output)
{
    private readonly ILogger<LinuxContainment> _logger = new XunitContainmentLogger(output);

    private static bool CgroupV2Available()
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        // cgroup v2 unified hierarchy indicator
        if (!File.Exists("/sys/fs/cgroup/cgroup.controllers"))
        {
            return false;
        }

        // Check that we can write to our own cgroup slice
        try
        {
            var content = File.ReadAllText("/proc/self/cgroup").Trim();

            foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("0::", StringComparison.Ordinal))
                {
                    var relativePath = line["0::".Length..].Trim();
                    var selfCgroup = Path.Combine("/sys/fs/cgroup", relativePath.TrimStart('/'));
                    var probeDir = Path.Combine(selfCgroup, "collabhost", ".test-probe");

                    Directory.CreateDirectory(probeDir);
                    Directory.Delete(probeDir);

                    return true;
                }
            }
        }
        catch (Exception) when (!Debugger.IsAttached)
        {
            // cgroup v2 not available or not writable
        }

        return false;
    }

    private static void SkipIfNotLinuxWithCgroup()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux only");
        Skip.IfNot(CgroupV2Available(), "cgroup v2 not available or not writable");
    }

    private static string GetSelfCgroupPath()
    {
        var content = File.ReadAllText("/proc/self/cgroup").Trim();

        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("0::", StringComparison.Ordinal))
            {
                var relativePath = line["0::".Length..].Trim();

                return Path.Combine("/sys/fs/cgroup", relativePath.TrimStart('/'));
            }
        }

        throw new InvalidOperationException("Could not determine self cgroup path");
    }

    [SkippableFact]
    [Trait("Platform", "linux")]
    public void CreateContainer_CreatesCgroupDirectory()
    {
        SkipIfNotLinuxWithCgroup();

        using var containment = new LinuxContainment(_logger);

        var containerName = "test-create-" + Guid.NewGuid().ToString("N")[..8];

        using var handle = containment.CreateContainer(containerName);

        handle.ShouldNotBeNull();

        // Verify the cgroup directory was created
        var cgroupPath = Path.Combine(GetSelfCgroupPath(), "collabhost", containerName);

        Directory.Exists(cgroupPath).ShouldBeTrue();
    }

    [SkippableFact]
    [Trait("Platform", "linux")]
    public void AssignProcess_WritesPidToCgroupProcs()
    {
        SkipIfNotLinuxWithCgroup();

        using var containment = new LinuxContainment(_logger);

        var containerName = "test-assign-" + Guid.NewGuid().ToString("N")[..8];

        using var handle = containment.CreateContainer(containerName);

        handle.ShouldNotBeNull();

        // Start a real process to assign
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "sleep",
            Arguments = "300",
            UseShellExecute = false,
            CreateNoWindow = true
        });

        process.ShouldNotBeNull();

        try
        {
            var assigned = handle.AssignProcess(process.Id);

            assigned.ShouldBeTrue();
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                process.WaitForExit(5000);
            }
        }
    }

    [SkippableFact]
    [Trait("Platform", "linux")]
    public void Terminate_KillsAllProcessesInCgroup()
    {
        SkipIfNotLinuxWithCgroup();

        using var containment = new LinuxContainment(_logger);

        var containerName = "test-terminate-" + Guid.NewGuid().ToString("N")[..8];

        using var handle = containment.CreateContainer(containerName);

        handle.ShouldNotBeNull();

        // Start a process and assign it to the cgroup
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "sleep",
            Arguments = "300",
            UseShellExecute = false,
            CreateNoWindow = true
        });

        process.ShouldNotBeNull();

        handle.AssignProcess(process.Id);

        // Terminate via cgroup -- exitCode is ignored on Linux
        handle.Terminate(0);

        // Give the kernel a moment to deliver SIGKILL
        process.WaitForExit(5000);

        process.HasExited.ShouldBeTrue();
    }

    [SkippableFact]
    [Trait("Platform", "linux")]
    public void Dispose_RemovesCgroupDirectory()
    {
        SkipIfNotLinuxWithCgroup();

        var selfCgroup = GetSelfCgroupPath();
        var containerName = "test-dispose-" + Guid.NewGuid().ToString("N")[..8];
        var cgroupPath = Path.Combine(selfCgroup, "collabhost", containerName);

        using (var containment = new LinuxContainment(_logger))
        {
            var handle = containment.CreateContainer(containerName);

            handle.ShouldNotBeNull();

            Directory.Exists(cgroupPath).ShouldBeTrue();

            // Dispose the handle first (no live processes, so rmdir will succeed)
            handle.Dispose();
        }

        // After containment disposal, the cgroup directory should be cleaned up
        Directory.Exists(cgroupPath).ShouldBeFalse();
    }

    [SkippableFact]
    [Trait("Platform", "linux")]
    public void IsSupported_KillOnClose_WhenCgroupAvailable_ReturnsTrue()
    {
        SkipIfNotLinuxWithCgroup();

        using var containment = new LinuxContainment(_logger);

        containment.IsSupported(ContainmentCapability.KillOnClose).ShouldBeTrue();
    }

    [SkippableFact]
    [Trait("Platform", "linux")]
    public void IsSupported_NonKillCapabilities_ReturnsFalse()
    {
        SkipIfNotLinuxWithCgroup();

        using var containment = new LinuxContainment(_logger);

        containment.IsSupported(ContainmentCapability.CpuLimit).ShouldBeFalse();
        containment.IsSupported(ContainmentCapability.MemoryLimit).ShouldBeFalse();
        containment.IsSupported(ContainmentCapability.ResourceAccounting).ShouldBeFalse();
    }

    [SkippableFact]
    [Trait("Platform", "linux")]
    public async Task CreateContainer_StartsOrphanKillWatcherInsideCgroup()
    {
        SkipIfNotLinuxWithCgroup();
        Skip.IfNot(SetprivAvailable(), "setpriv --pdeathsig not available");

        using var containment = new LinuxContainment(_logger);

        var containerName = "test-watcher-" + Guid.NewGuid().ToString("N")[..8];

        using var handle = containment.CreateContainer(containerName);

        handle.ShouldNotBeNull();

        // The watcher should be inside the cgroup. Read cgroup.procs and verify
        // there is at least one PID present (the watcher) before any workload is assigned.
        var cgroupPath = Path.Combine(GetSelfCgroupPath(), "collabhost", containerName);
        var procsPath = Path.Combine(cgroupPath, "cgroup.procs");

        // Give the watcher a moment to be moved into the cgroup
        await Task.Delay(200);

        var procs = (await File.ReadAllTextAsync(procsPath)).Trim();

        procs.ShouldNotBeNullOrEmpty();

        // At least one PID should be the watcher; verify each listed PID is alive
        var pids = procs.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        pids.Length.ShouldBeGreaterThanOrEqualTo(1);

        foreach (var pidStr in pids)
        {
            int.TryParse(pidStr, System.Globalization.CultureInfo.InvariantCulture, out var pid).ShouldBeTrue();

            Directory.Exists($"/proc/{pid.ToString(System.Globalization.CultureInfo.InvariantCulture)}").ShouldBeTrue();
        }
    }

    [SkippableFact]
    [Trait("Platform", "linux")]
    public async Task OrphanKillMechanism_KillsCgroupOnAbruptParentDeath()
    {
        SkipIfNotLinuxWithCgroup();
        Skip.IfNot(SetprivAvailable(), "setpriv --pdeathsig not available");

        // This test verifies the kernel-level mechanism that LinuxContainment relies on:
        // setpriv --pdeathsig SIGTERM + bash trap + cgroup.kill atomically tears down a
        // cgroup when an upstream parent dies abruptly (SIGKILL).
        //
        // We cannot SIGKILL the test runner itself, so the test stages a small fake
        // supervisor (bash) that:
        //   1. Creates a cgroup directory under /sys/fs/cgroup/.../collabhost-test/<name>/
        //   2. Spawns the same setpriv-watcher chain LinuxContainment uses
        //   3. Spawns a workload (sleep) and a grandchild (also sleep) into the cgroup
        //   4. Execs sleep itself so we can SIGKILL it
        //
        // The test then SIGKILLs the bash supervisor and asserts that within a few
        // seconds, every PID listed in cgroup.procs is dead and the cgroup directory
        // is rmdir-able (empty of members).
        var selfCgroup = GetSelfCgroupPath();
        var containerName = "test-orphan-" + Guid.NewGuid().ToString("N")[..8];
        var cgroupPath = Path.Combine(selfCgroup, "collabhost-test", containerName);
        var killPath = Path.Combine(cgroupPath, "cgroup.kill");
        var procsPath = Path.Combine(cgroupPath, "cgroup.procs");

        Directory.CreateDirectory(cgroupPath);

        try
        {
            // Bash supervisor script. Joins itself + spawns watcher + workload + grandchild.
            // The watcher uses the same `sleep & wait $!` shape as production code so
            // bash's SIGTERM handler runs promptly (foreground sleep would block it).
            var script = $$"""
                echo $$ > {{procsPath}}
                setpriv --pdeathsig SIGTERM bash -c 'trap "echo 1 > {{killPath}}; exit 0" SIGTERM; while :; do sleep 86400 & wait $!; done' &
                bash -c 'sleep 600 & wait' &
                exec sleep 600
                """;

            using var supervisor = Process.Start(new ProcessStartInfo
            {
                FileName = "bash",
                ArgumentList = { "-c", script },
                UseShellExecute = false,
                CreateNoWindow = true
            });

            supervisor.ShouldNotBeNull();

            // Wait for the supervisor and its descendants to settle into the cgroup
            await Task.Delay(500);

            var procsBeforeKill = (await File.ReadAllTextAsync(procsPath)).Trim();
            var pidsBeforeKill = procsBeforeKill
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.Parse(s, System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();

            var supervisorPidStr = supervisor.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var pidsString = string.Join(", ", pidsBeforeKill.Select(p => p.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            output.WriteLine($"PIDs in cgroup before SIGKILL of supervisor {supervisorPidStr}: [{pidsString}]");

            // The cgroup should contain at least the supervisor + watcher + workload (2+).
            pidsBeforeKill.Length.ShouldBeGreaterThanOrEqualTo(2);

            // SIGKILL the supervisor abruptly
            LinuxNativeMethods.Kill(supervisor.Id, LinuxNativeMethods.SIGKILL);

            // Allow time for: kernel to reap supervisor → kernel to send PDEATHSIG to watcher
            // → watcher trap to execute → cgroup.kill to atomically SIGKILL all members
            // → kernel to clean up exited processes from cgroup.procs
            var deadline = DateTime.UtcNow.AddSeconds(10);
            string procsAfter;

            do
            {
                await Task.Delay(200);
                procsAfter = File.Exists(procsPath)
                    ? (await File.ReadAllTextAsync(procsPath)).Trim()
                    : "";
            }
            while (procsAfter.Length > 0 && DateTime.UtcNow < deadline);

            output.WriteLine($"cgroup.procs after kill: '{procsAfter}'");

            procsAfter.ShouldBe("", "all PIDs in the cgroup should have been SIGKILL'd by the watcher's trap");

            // Verify each PID we observed is genuinely dead
            foreach (var pid in pidsBeforeKill)
            {
                var pidStr = pid.ToString(System.Globalization.CultureInfo.InvariantCulture);

                Directory.Exists($"/proc/{pidStr}").ShouldBeFalse($"PID {pidStr} should be dead");
            }
        }
        finally
        {
            try
            {
                Directory.Delete(cgroupPath);
            }
            catch (IOException)
            {
                // Best-effort cleanup
            }
        }
    }

    private static bool SetprivAvailable()
    {
        try
        {
            using var probe = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "setpriv",
                    Arguments = "--help",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            probe.Start();
            probe.WaitForExit(TimeSpan.FromSeconds(5));

            var helpText = probe.StandardOutput.ReadToEnd() + probe.StandardError.ReadToEnd();

            return helpText.Contains("--pdeathsig", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    [SkippableFact]
    [Trait("Platform", "linux")]
    public void CleanupStaleCgroups_RemovesOrphanedDirectories()
    {
        SkipIfNotLinuxWithCgroup();

        var selfCgroup = GetSelfCgroupPath();

        // Create a fake stale cgroup directory before constructing LinuxContainment
        var staleName = "stale-" + Guid.NewGuid().ToString("N")[..8];
        var stalePath = Path.Combine(selfCgroup, "collabhost", staleName);

        Directory.CreateDirectory(stalePath);

        Directory.Exists(stalePath).ShouldBeTrue();

        // Constructing LinuxContainment triggers CleanupStaleCgroups
        using var containment = new LinuxContainment(_logger);

        // The stale directory should have been cleaned up during construction
        Directory.Exists(stalePath).ShouldBeFalse();
    }
}

// No subclasses expected -- test adapter bridging ILogger<T> to xUnit output
file sealed class XunitContainmentLogger(ITestOutputHelper output) : ILogger<LinuxContainment>
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
