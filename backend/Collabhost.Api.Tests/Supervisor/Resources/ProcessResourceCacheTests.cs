using Collabhost.Api.Supervisor.Resources;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Supervisor.Resources;

public class ProcessResourceCacheTests
{
    [Fact]
    public void GetLatest_NoEntry_ReturnsNull()
    {
        var cache = new ProcessResourceCache();

        cache.GetLatest(Ulid.NewUlid()).ShouldBeNull();
    }

    [Fact]
    public void Set_ThenGet_ReturnsSnapshot()
    {
        var cache = new ProcessResourceCache();
        var appId = Ulid.NewUlid();
        var snapshot = new ProcessResourceSnapshot(12.5, 256.0, 42, DateTime.UtcNow);

        cache.Set(appId, snapshot);

        cache.GetLatest(appId).ShouldBe(snapshot);
    }

    [Fact]
    public void Set_TwiceForSameAppId_ReturnsLatest()
    {
        var cache = new ProcessResourceCache();
        var appId = Ulid.NewUlid();
        var first = new ProcessResourceSnapshot(5.0, 100.0, 10, DateTime.UtcNow);
        var second = new ProcessResourceSnapshot(7.0, 110.0, 12, DateTime.UtcNow.AddSeconds(5));

        cache.Set(appId, first);
        cache.Set(appId, second);

        cache.GetLatest(appId).ShouldBe(second);
    }

    [Fact]
    public void Remove_DropsEntry()
    {
        var cache = new ProcessResourceCache();
        var appId = Ulid.NewUlid();
        cache.Set(appId, new ProcessResourceSnapshot(1.0, 1.0, 1, DateTime.UtcNow));

        cache.Remove(appId);

        cache.GetLatest(appId).ShouldBeNull();
    }
}
