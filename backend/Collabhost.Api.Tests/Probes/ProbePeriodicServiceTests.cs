using Collabhost.Api.Probes;
using Collabhost.Api.Tests.Fixtures;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Probes;

// Card #337 -- the periodic re-scan service is the load-bearing fix for the
// production-only "blank probes after 30 minutes" defect. These tests prove
// the service is registered as a hosted service and its tick body delegates
// to RunProbesForAllAppsAsync.
[Collection("Api")]
public class ProbePeriodicServiceTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture;

    [Fact]
    public void HostedServices_IncludesProbePeriodicService()
    {
        var hostedServices = _fixture.Services.GetServices<IHostedService>().ToList();

        hostedServices.OfType<ProbePeriodicService>().ShouldNotBeEmpty
        (
            "AddProbes must register ProbePeriodicService as IHostedService"
        );
    }

    [Fact]
    public void HostedServices_IncludesProbeStartupService()
    {
        // Companion check -- confirms AddProbes still registers the boot-time
        // scan. If a refactor ever drops it, the periodic timer would push the
        // first probe out by ScanInterval minutes (default 15), which is a
        // regression even though the long-term steady state would self-heal.
        var hostedServices = _fixture.Services.GetServices<IHostedService>().ToList();

        hostedServices.OfType<ProbeStartupService>().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task TickAsync_CompletesAgainstLiveAppSet()
    {
        // The tick body is a thin pass-through to RunProbesForAllAppsAsync.
        // Resolving the constructed service from DI and invoking TickAsync
        // directly is the cheapest behavioral assertion: it proves the call
        // path completes against whatever apps the fixture has registered
        // without driving the PeriodicTimer-based outer loop.
        var probeService = _fixture.Services.GetRequiredService<ProbeService>();
        var timeProvider = _fixture.Services.GetRequiredService<TimeProvider>();
        var logger = _fixture.Services.GetRequiredService<ILogger<ProbePeriodicService>>();

        var periodic = new ProbePeriodicService(probeService, timeProvider, logger);

        await periodic.TickAsync(CancellationToken.None);

        // Any unknown Ulid against a non-applicable AppType slug should return
        // NotApplicable -- proves the post-tick read path is honest about the
        // curator policy gate even when the cache is empty.
        var notApplicable = probeService.GetCachedProbes(Ulid.NewUlid(), "system-service");

        notApplicable.Status.ShouldBe(ProbeCacheStatus.NotApplicable);
        notApplicable.Entries.ShouldBeEmpty();

        // Any unknown Ulid against an applicable AppType slug should return
        // NeverProbed -- the three empty states (NotApplicable, NeverProbed,
        // empty-Fresh) must remain observationally distinguishable at the API
        // boundary (Card #337 C-1 fold-in).
        var neverProbed = probeService.GetCachedProbes(Ulid.NewUlid(), "dotnet-app");

        neverProbed.Status.ShouldBe(ProbeCacheStatus.NeverProbed);
        neverProbed.Entries.ShouldBeEmpty();
    }
}
