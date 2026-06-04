using System.Runtime.Versioning;

using Collabhost.Api.Supervisor.Resources;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Supervisor.Resources;

[SupportedOSPlatform("linux")]
public class LinuxProcessResourceSamplerTests
{
    [Fact]
    [Trait("Platform", "linux")]
    public void Sample_CurrentProcess_ReturnsMemoryAndHandleCount()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux only");

        var sampler = new LinuxProcessResourceSampler(NullLogger<LinuxProcessResourceSampler>.Instance);

        var snapshot = sampler.Sample(Environment.ProcessId);

        snapshot.ShouldNotBeNull();
        snapshot.MemoryMb.ShouldBeGreaterThan(0);
        snapshot.HandleCount.ShouldNotBeNull();
        snapshot.HandleCount.Value.ShouldBeGreaterThan(0);
    }

    [Fact]
    [Trait("Platform", "linux")]
    public void Sample_FirstCall_ReturnsNullCpuPercent()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux only");

        var sampler = new LinuxProcessResourceSampler(NullLogger<LinuxProcessResourceSampler>.Instance);

        var snapshot = sampler.Sample(Environment.ProcessId);

        snapshot.ShouldNotBeNull();
        snapshot.CpuPercent.ShouldBeNull();
    }

    [Fact]
    [Trait("Platform", "linux")]
    public async Task Sample_SecondCall_ReturnsCpuPercent()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux only");

        var sampler = new LinuxProcessResourceSampler(NullLogger<LinuxProcessResourceSampler>.Instance);
        var pid = Environment.ProcessId;

        sampler.Sample(pid);

        // Wait at least one wall-clock tick so the delta is meaningful.
        await Task.Delay(50);

        var snapshot = sampler.Sample(pid);

        snapshot.ShouldNotBeNull();
        snapshot.CpuPercent.ShouldNotBeNull();
        snapshot.CpuPercent.Value.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    [Trait("Platform", "linux")]
    public void Sample_NonexistentPid_ReturnsNull()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux only");

        var sampler = new LinuxProcessResourceSampler(NullLogger<LinuxProcessResourceSampler>.Instance);

        // Pick a high PID unlikely to be in use; if it happens to be alive the
        // assertion will fail loudly rather than silently pass.
        var snapshot = sampler.Sample(2_000_000);

        snapshot.ShouldBeNull();
    }

    [Fact]
    [Trait("Platform", "linux")]
    public void Forget_RemovesPriorSample()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux only");

        var sampler = new LinuxProcessResourceSampler(NullLogger<LinuxProcessResourceSampler>.Instance);
        var pid = Environment.ProcessId;

        sampler.Sample(pid);

        sampler.Forget(pid);

        var snapshot = sampler.Sample(pid);

        snapshot.ShouldNotBeNull();
        snapshot.CpuPercent.ShouldBeNull();
    }
}
