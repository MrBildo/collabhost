using System.Diagnostics;
using System.Runtime.Versioning;

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
                process.Kill(entireProcessTree: true);
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
