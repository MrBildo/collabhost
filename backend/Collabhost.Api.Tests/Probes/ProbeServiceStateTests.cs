using Collabhost.Api.Probes;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Probes;

// State-machine coverage for the four ProbeCacheStatus values exposed at the
// /apps/{slug} wire boundary -- Card #337. The fresh-vs-stale split is the only
// branch exercised end-to-end here because IsProbeApplicable + the cache-miss
// path are pure functions with no DI graph; populating the cache to exercise
// Fresh and Stale via GetCachedProbes itself lives in ProbeEndpointTests where
// the full app + startup-probe pipeline runs.
public class ProbeServiceStateTests
{
    [Theory]
    [InlineData("dotnet-app", true)]
    [InlineData("nodejs-app", true)]
    [InlineData("static-site", true)]
    [InlineData("executable", true)]
    [InlineData("system-service", false)]
    public void IsProbeApplicable_KnownAppTypes_MatchCuratorPolicy(string appTypeSlug, bool expected) =>
        ProbeService.IsProbeApplicable(appTypeSlug).ShouldBe(expected);

    [Fact]
    public void IsProbeApplicable_UnknownUserAppType_DefaultsToApplicable() =>
        // User-defined / unknown AppType slugs fall through to "applicable" so
        // the curator (not this gate) is the authoritative decision on whether
        // any panel comes out.
        ProbeService.IsProbeApplicable("custom-user-defined-type").ShouldBeTrue();

    [Fact]
    public void ClassifyAge_AtZero_IsFresh() =>
        ProbeService.ClassifyAge(TimeSpan.Zero, TimeSpan.FromMinutes(30))
            .ShouldBe(ProbeCacheStatus.Fresh);

    [Fact]
    public void ClassifyAge_InsideWindow_IsFresh() =>
        ProbeService.ClassifyAge(TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(30))
            .ShouldBe(ProbeCacheStatus.Fresh);

    [Fact]
    public void ClassifyAge_AtWindow_IsFresh() =>
        // Boundary inclusive on Fresh side -- mirrors the production 15-min
        // refresher / 30-min window relationship: a refresher tick that lands
        // exactly at the window boundary should not be reported as Stale.
        ProbeService.ClassifyAge(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30))
            .ShouldBe(ProbeCacheStatus.Fresh);

    [Fact]
    public void ClassifyAge_BeyondWindow_IsStale() =>
        ProbeService.ClassifyAge(TimeSpan.FromMinutes(31), TimeSpan.FromMinutes(30))
            .ShouldBe(ProbeCacheStatus.Stale);
}
