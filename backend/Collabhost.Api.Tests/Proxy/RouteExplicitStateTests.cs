using Collabhost.Api.ActivityLog;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Data;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;
using Collabhost.Api.Supervisor;
using Collabhost.Api.Supervisor.Containment;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Proxy;

public class RouteExplicitStateTests
{
    [Fact]
    public void IsRouteExplicitlyEnabled_UnknownSlug_ReturnsFalse()
    {
        var manager = CreateProxyManager();

        manager.IsRouteExplicitlyEnabled("never-registered").ShouldBeFalse();
    }

    [Fact]
    public void IsRouteExplicitlyEnabled_AfterEnableRoute_ReturnsTrue()
    {
        var manager = CreateProxyManager();

        manager.EnableRoute("my-static-site");

        manager.IsRouteExplicitlyEnabled("my-static-site").ShouldBeTrue();
    }

    [Fact]
    public void IsRouteExplicitlyEnabled_AfterDisableRoute_ReturnsFalse()
    {
        var manager = CreateProxyManager();

        manager.EnableRoute("my-static-site");
        manager.DisableRoute("my-static-site");

        manager.IsRouteExplicitlyEnabled("my-static-site").ShouldBeFalse();
    }

    [Fact]
    public void IsRouteEnabled_VsExplicit_DifferForUnknownSlug()
    {
        var manager = CreateProxyManager();

        // IsRouteEnabled returns true for unknowns (default-on for route building)
        manager.IsRouteEnabled("unknown").ShouldBeTrue();

        // IsRouteExplicitlyEnabled returns false (no explicit action taken)
        manager.IsRouteExplicitlyEnabled("unknown").ShouldBeFalse();
    }

    [Fact]
    public void IsRouteExplicitlyEnabled_MultipleRoutes_TracksSeparately()
    {
        var manager = CreateProxyManager();

        manager.EnableRoute("site-a");
        manager.DisableRoute("site-b");

        manager.IsRouteExplicitlyEnabled("site-a").ShouldBeTrue();
        manager.IsRouteExplicitlyEnabled("site-b").ShouldBeFalse();
        manager.IsRouteExplicitlyEnabled("site-c").ShouldBeFalse();
    }

    private static ProxyManager CreateProxyManager()
    {
        var dbFactory = new FakeDbContextFactory();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var appStore = new AppStore(dbFactory, cache, NullLogger<AppStore>.Instance);

        var typeStore = new TypeStore
        (
            new Collabhost.Api.Events.EventBus<TypeStoreReloadedEvent>(),
            new TypeStoreSettings { UserTypesDirectory = Path.Combine(Path.GetTempPath(), "collabhost-test-notexist") },
            new ProxySettings { BaseDomain = "collab.internal", BinaryPath = "caddy", ListenAddress = ":443", CertLifetime = "168h", SelfPort = 58400 },
            NullLogger<TypeStore>.Instance
        );
        var capabilityStore = new CapabilityStore(typeStore, appStore, NullLogger<CapabilityStore>.Instance);

        var runner = new FakeManagedProcessRunner();
        var eventBus = new Collabhost.Api.Events.EventBus<Collabhost.Api.Events.ProcessStateChangedEvent>();
        var activityEventStore = new ActivityEventStore(dbFactory, NullLogger<ActivityEventStore>.Instance);
        var supervisor = new ProcessSupervisor(runner, new NullContainment(), appStore, capabilityStore, typeStore, eventBus, [], activityEventStore, NullLogger<ProcessSupervisor>.Instance);

        return new ProxyManager
        (
            new FakeCaddyClient(),
            appStore,
            capabilityStore,
            typeStore,
            supervisor,
            eventBus,
            new ProxySettings
            {
                BaseDomain = "collab.internal",
                BinaryPath = "caddy",
                ListenAddress = ":443",
                CertLifetime = "168h",
                SelfPort = 58400,
                AdminPort = 2019
            },
            activityEventStore,
            NullLogger<ProxyManager>.Instance
        );
    }
}

// Sealed to satisfy MA0053 -- test fakes with no subtype need
file sealed class FakeCaddyClient : ICaddyClient
{
    public Task<bool> IsReadyAsync(CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<bool> LoadConfigAsync(System.Text.Json.Nodes.JsonObject config, CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<System.Text.Json.Nodes.JsonObject?> GetConfigAsync(CancellationToken ct = default) =>
        Task.FromResult<System.Text.Json.Nodes.JsonObject?>(null);
}

file sealed class FakeDbContextFactory : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() =>
        throw new NotSupportedException("Not used in route-state tests");
#pragma warning disable VSTHRD200 // Async method naming -- interface implementation
    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used in route-state tests");
#pragma warning restore VSTHRD200
}

file sealed class FakeManagedProcessRunner : IManagedProcessRunner
{
    public IProcessHandle Start(ProcessStartConfiguration configuration) =>
        throw new NotSupportedException("Not used in route-state tests");

    public Task<ProcessRunResult> RunToCompletionAsync
    (
        ProcessStartConfiguration configuration,
        TimeSpan timeout,
        CancellationToken ct = default
    ) =>
        throw new NotSupportedException("Not used in route-state tests");
}
