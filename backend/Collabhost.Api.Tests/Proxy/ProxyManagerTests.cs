using Collabhost.Api.ActivityLog;
using Collabhost.Api.Data;
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

public class ProxyManagerTests
{
    [Fact]
    public void IsRouteEnabled_UnknownSlug_ReturnsTrue()
    {
        var manager = CreateProxyManager();

        manager.IsRouteEnabled("unknown-app").ShouldBeTrue();
    }

    [Fact]
    public void DisableRoute_ThenIsRouteEnabled_ReturnsFalse()
    {
        var manager = CreateProxyManager();

        manager.DisableRoute("my-app");

        manager.IsRouteEnabled("my-app").ShouldBeFalse();
    }

    [Fact]
    public void EnableRoute_AfterDisable_ReturnsTrue()
    {
        var manager = CreateProxyManager();

        manager.DisableRoute("my-app");
        manager.EnableRoute("my-app");

        manager.IsRouteEnabled("my-app").ShouldBeTrue();
    }

    [Fact]
    public void DisableRoute_NullSlug_Throws() =>
        Should.Throw<ArgumentException>(() => CreateProxyManager().DisableRoute(null!));

    [Fact]
    public void EnableRoute_WhitespaceSlug_Throws() =>
        Should.Throw<ArgumentException>(() => CreateProxyManager().EnableRoute("  "));

    [Fact]
    public void IsRouteEnabled_MultipleRoutes_TracksSeparately()
    {
        var manager = CreateProxyManager();

        manager.DisableRoute("app-a");
        manager.EnableRoute("app-b");

        manager.IsRouteEnabled("app-a").ShouldBeFalse();
        manager.IsRouteEnabled("app-b").ShouldBeTrue();
    }

    [Fact]
    public void EnableRoute_AfterDisable_ThenRequestSync_DoesNotThrow()
    {
        var manager = CreateProxyManager();

        manager.DisableRoute("my-app");
        manager.IsRouteEnabled("my-app").ShouldBeFalse();

        manager.EnableRoute("my-app");
        manager.IsRouteEnabled("my-app").ShouldBeTrue();

        Should.NotThrow(() => manager.RequestSync());
    }

    [Fact]
    public void DisableRoute_ThenRequestSync_DoesNotThrow()
    {
        var manager = CreateProxyManager();

        manager.DisableRoute("static-site");
        manager.IsRouteEnabled("static-site").ShouldBeFalse();

        Should.NotThrow(() => manager.RequestSync());
    }

    [Fact]
    public void RequestSync_DoesNotThrow() =>
        Should.NotThrow(() => CreateProxyManager().RequestSync());

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var manager = CreateProxyManager();

        Should.NotThrow(() => manager.Dispose());
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var manager = CreateProxyManager();

        manager.Dispose();

        Should.NotThrow(() => manager.Dispose());
    }

    private static ProxyManager CreateProxyManager()
    {
        // Create minimal real dependencies for route-state-only tests (StartAsync is never called)
        var dbFactory = new FakeDbContextFactory();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var appStore = new AppStore(dbFactory, cache, NullLogger<AppStore>.Instance);

        var runner = new FakeManagedProcessRunner();
        var eventBus = new Collabhost.Api.Events.EventBus<Collabhost.Api.Events.ProcessStateChangedEvent>();
        var activityEventStore = new ActivityEventStore(dbFactory, NullLogger<ActivityEventStore>.Instance);
        var supervisor = new ProcessSupervisor(runner, new NullContainment(), appStore, eventBus, [], activityEventStore, NullLogger<ProcessSupervisor>.Instance);

        return new ProxyManager
        (
            new FakeCaddyClient(),
            appStore,
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
