using Collabhost.Api.Supervisor.Resources;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Supervisor.Resources;

public class NullProcessResourceSamplerTests
{
    [Fact]
    public void Sample_ReturnsNullForAnyPid()
    {
        var sampler = new NullProcessResourceSampler();

        sampler.Sample(1).ShouldBeNull();
        sampler.Sample(99999).ShouldBeNull();
    }

    [Fact]
    public void Forget_DoesNotThrow()
    {
        var sampler = new NullProcessResourceSampler();

        // No state to forget; the call must be a no-op.
        Should.NotThrow(() => sampler.Forget(1));
    }
}
