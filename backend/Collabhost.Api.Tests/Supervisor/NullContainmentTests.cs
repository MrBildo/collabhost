using Collabhost.Api.Supervisor.Containment;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Supervisor;

public class NullContainmentTests
{
    [Fact]
    public void CreateContainer_ReturnsNull()
    {
        var containment = new NullContainment();

        var handle = containment.CreateContainer("test-app");

        handle.ShouldBeNull();
    }

    [Theory]
    [InlineData(ContainmentCapability.KillOnClose)]
    [InlineData(ContainmentCapability.CpuLimit)]
    [InlineData(ContainmentCapability.MemoryLimit)]
    [InlineData(ContainmentCapability.ResourceAccounting)]
    public void IsSupported_ReturnsFalseForAll(ContainmentCapability capability)
    {
        var containment = new NullContainment();

        var supported = containment.IsSupported(capability);

        supported.ShouldBeFalse();
    }
}
