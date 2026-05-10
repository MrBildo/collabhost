using System.Diagnostics;
using System.Runtime.Versioning;

using Collabhost.Api.Supervisor.Resources;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Supervisor.Resources;

[SupportedOSPlatform("windows")]
public class WindowsProcessResourceSamplerTests
{
    [SkippableFact]
    [Trait("Platform", "windows")]
    public void Sample_RunningProcess_ReturnsMemoryAndHandleCount()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");

        var sampler = new WindowsProcessResourceSampler(NullLogger<WindowsProcessResourceSampler>.Instance);

        var snapshot = sampler.Sample(Environment.ProcessId);

        snapshot.ShouldNotBeNull();
        snapshot.MemoryMb.ShouldBeGreaterThan(0);
        snapshot.HandleCount.ShouldNotBeNull();
        snapshot.HandleCount.Value.ShouldBeGreaterThan(0);
    }

    [SkippableFact]
    [Trait("Platform", "windows")]
    public void Sample_FirstCall_ReturnsNullCpuPercent()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");

        var sampler = new WindowsProcessResourceSampler(NullLogger<WindowsProcessResourceSampler>.Instance);

        var snapshot = sampler.Sample(Environment.ProcessId);

        snapshot.ShouldNotBeNull();

        // First sample for a PID has no prior measurement to delta against.
        snapshot.CpuPercent.ShouldBeNull();
    }

    [SkippableFact]
    [Trait("Platform", "windows")]
    public void Sample_SecondCall_ReturnsCpuPercent()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");

        var sampler = new WindowsProcessResourceSampler(NullLogger<WindowsProcessResourceSampler>.Instance);
        var pid = Environment.ProcessId;

        sampler.Sample(pid);

        // Burn a tiny bit of CPU between samples so the second call has something
        // to measure. Without this the delta could be exactly zero.
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < 50)
        {
            // Spin
        }

        var snapshot = sampler.Sample(pid);

        snapshot.ShouldNotBeNull();
        snapshot.CpuPercent.ShouldNotBeNull();
        snapshot.CpuPercent.Value.ShouldBeGreaterThanOrEqualTo(0);
    }

    [SkippableFact]
    [Trait("Platform", "windows")]
    public void Sample_NonexistentPid_ReturnsNull()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");

        var sampler = new WindowsProcessResourceSampler(NullLogger<WindowsProcessResourceSampler>.Instance);

        // PID 0xFFFFFFFE is reserved for the System Idle Process on Windows; using a
        // very large but valid-shape integer that no real process will own.
        var snapshot = sampler.Sample(int.MaxValue - 1);

        snapshot.ShouldBeNull();
    }

    [SkippableFact]
    [Trait("Platform", "windows")]
    public void Forget_RemovesPriorSample()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");

        var sampler = new WindowsProcessResourceSampler(NullLogger<WindowsProcessResourceSampler>.Instance);
        var pid = Environment.ProcessId;

        sampler.Sample(pid);

        sampler.Forget(pid);

        // After Forget, the next sample should look like a "first sample" again --
        // CpuPercent null because no prior delta is available.
        var snapshot = sampler.Sample(pid);

        snapshot.ShouldNotBeNull();
        snapshot.CpuPercent.ShouldBeNull();
    }
}
