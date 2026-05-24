using System.Collections.Concurrent;
using System.Reflection;
using Collabhost.Api.ActivityLog;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Data;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Events;
using Collabhost.Api.Platform;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;
using Collabhost.Api.Supervisor;
using Collabhost.Api.Supervisor.Containment;
using Collabhost.Api.Tests.Fixtures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Collabhost.Api.Tests.Registry;

// Non-file interface so the `CreateSupervisor` helper can expose StartCallCount to
// tests without leaking a file-local type into a non-file-local method signature
// (CS9051). The recording runner is still file-local; tests interact with it
// strictly through this surface.
public interface IRecordingRunner
{
    int StartCallCount { get; }
}

// Card #350 -- persisted operator-stop intent across Collabhost restart. Covers the
// AppStore write-through helper, the ProxyManager boot hydration scoped to routing-only
// AppTypes, and the ProcessSupervisor auto-start suppression. CA1001 mirrors the
// sibling proxy tests -- async cleanup via IAsyncLifetime owns the SqliteConnection.
#pragma warning disable CA1001
public class StoppedByOperatorPersistenceTests : IAsyncLifetime
#pragma warning restore CA1001
{
    private SqliteConnection _connection = default!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var dbFactory = new TestDbContextFactory(_connection);

        await using var context = await dbFactory.CreateDbContextAsync();
        await context.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => _connection.DisposeAsync().AsTask();

    // --- AppStore write-through ---

    [Fact]
    public async Task SetStoppedByOperator_PersistsAcrossContext()
    {
        var dbFactory = new TestDbContextFactory(_connection);
        var appStore = new AppStore(dbFactory, new MemoryCache(new MemoryCacheOptions()), NullLogger<AppStore>.Instance);

        var app = await SeedAppAsync(dbFactory, "site-a", "static-site");

        await appStore.SetStoppedByOperatorAsync(app.Id, app.Slug, true, CancellationToken.None);

        // Re-read via a fresh context to prove the value committed (cache cleared by helper).
        var refreshed = await appStore.GetByIdAsync(app.Id, CancellationToken.None);

        refreshed.ShouldNotBeNull();
        refreshed.StoppedByOperator.ShouldBeTrue();
    }

    [Fact]
    public async Task SetStoppedByOperator_FalseClearsPriorTrue()
    {
        var dbFactory = new TestDbContextFactory(_connection);
        var appStore = new AppStore(dbFactory, new MemoryCache(new MemoryCacheOptions()), NullLogger<AppStore>.Instance);

        var app = await SeedAppAsync(dbFactory, "site-b", "static-site", stoppedByOperator: true);

        await appStore.SetStoppedByOperatorAsync(app.Id, app.Slug, false, CancellationToken.None);

        var refreshed = await appStore.GetByIdAsync(app.Id, CancellationToken.None);

        refreshed.ShouldNotBeNull();
        refreshed.StoppedByOperator.ShouldBeFalse();
    }

    [Fact]
    public async Task SetStoppedByOperator_DoesNotMutateOtherProperties()
    {
        var dbFactory = new TestDbContextFactory(_connection);
        var appStore = new AppStore(dbFactory, new MemoryCache(new MemoryCacheOptions()), NullLogger<AppStore>.Instance);

        var app = await SeedAppAsync(dbFactory, "site-c", "static-site", displayName: "Original Name");

        await appStore.SetStoppedByOperatorAsync(app.Id, app.Slug, true, CancellationToken.None);

        var refreshed = await appStore.GetByIdAsync(app.Id, CancellationToken.None);

        refreshed.ShouldNotBeNull();
        refreshed.DisplayName.ShouldBe("Original Name");
        refreshed.AppTypeSlug.ShouldBe("static-site");
        refreshed.StoppedByOperator.ShouldBeTrue();
    }

    [Fact]
    public async Task SetStoppedByOperator_InvalidatesSlugAndListCaches()
    {
        var dbFactory = new TestDbContextFactory(_connection);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var appStore = new AppStore(dbFactory, cache, NullLogger<AppStore>.Instance);

        var app = await SeedAppAsync(dbFactory, "site-d", "static-site");

        // Warm the slug + list caches with stale (=false) values.
        _ = await appStore.GetBySlugAsync(app.Slug, CancellationToken.None);
        _ = await appStore.ListAsync(CancellationToken.None);

        await appStore.SetStoppedByOperatorAsync(app.Id, app.Slug, true, CancellationToken.None);

        var bySlug = await appStore.GetBySlugAsync(app.Slug, CancellationToken.None);
        bySlug.ShouldNotBeNull();
        bySlug.StoppedByOperator.ShouldBeTrue();

        var listed = await appStore.ListAsync(CancellationToken.None);
        listed.Single(a => a.Id == app.Id).StoppedByOperator.ShouldBeTrue();
    }

    // --- ProxyManager hydration ---

    [Fact]
    public async Task HydrateRouteStatesFromPersistence_WritesOnlyFalseEntriesForRoutingOnlyApps()
    {
        var dbFactory = new TestDbContextFactory(_connection);

        await SeedAppAsync(dbFactory, "site-stopped", "static-site", stoppedByOperator: true);
        await SeedAppAsync(dbFactory, "site-running", "static-site", stoppedByOperator: false);

        var manager = await CreateProxyManagerAsync(dbFactory);

        await InvokeHydrateAsync(manager);

        var states = GetRouteStates(manager);

        // Only the stopped app gets a `false` entry; enabled apps stay implicit via
        // IsRouteEnabled's default-true fallback.
        states.ShouldContainKey("site-stopped");
        states["site-stopped"].ShouldBeFalse();
        states.ShouldNotContainKey("site-running");

        manager.IsRouteEnabled("site-stopped").ShouldBeFalse();
        manager.IsRouteEnabled("site-running").ShouldBeTrue();
    }

    [Fact]
    public async Task HydrateRouteStatesFromPersistence_SkipsProcessBearingRoutedApps()
    {
        var dbFactory = new TestDbContextFactory(_connection);

        // dotnet-app is process-bearing AND routed -- its operator-stopped signal is
        // 502 from a dead upstream, NOT a torn-down route. Hydration must NOT push
        // this slug into _routeStates.
        await SeedAppAsync(dbFactory, "api-stopped", "dotnet-app", stoppedByOperator: true);

        var manager = await CreateProxyManagerAsync(dbFactory);

        await InvokeHydrateAsync(manager);

        var states = GetRouteStates(manager);

        states.ShouldNotContainKey("api-stopped");
        manager.IsRouteEnabled("api-stopped").ShouldBeTrue();
    }

    [Fact]
    public async Task HydrateRouteStatesFromPersistence_EmptyDb_LeavesRouteStatesEmpty()
    {
        var dbFactory = new TestDbContextFactory(_connection);
        var manager = await CreateProxyManagerAsync(dbFactory);

        await InvokeHydrateAsync(manager);

        GetRouteStates(manager).ShouldBeEmpty();
    }

    [Fact]
    public async Task HydrateRouteStatesFromPersistence_HydratesExternalRoute()
    {
        var dbFactory = new TestDbContextFactory(_connection);

        await SeedAppAsync(dbFactory, "ext-stopped", "external-route", stoppedByOperator: true);

        var manager = await CreateProxyManagerAsync(dbFactory);

        await InvokeHydrateAsync(manager);

        // external-route is the operationally-visible case the card was filed for --
        // the route should NOT silently re-enable across restart.
        manager.IsRouteEnabled("ext-stopped").ShouldBeFalse();
    }

    // --- ProcessSupervisor auto-start suppression ---

    [Fact]
    public async Task ProcessSupervisor_StartAsync_SkipsAutoStartWhenStoppedByOperator()
    {
        var dbFactory = new TestDbContextFactory(_connection);

        // dotnet-app has auto-start.enabled=true by default but StoppedByOperator=true
        // should suppress the auto-start.
        await SeedAppAsync(dbFactory, "api-paused", "dotnet-app", stoppedByOperator: true);

        var (supervisor, runner) = await CreateSupervisorAsync(dbFactory);

        await supervisor.StartAsync(CancellationToken.None);

        supervisor.GetProcesses().ShouldBeEmpty();
        runner.StartCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task ProcessSupervisor_StartAsync_PreservesExistingAutoStartGate()
    {
        var dbFactory = new TestDbContextFactory(_connection);

        // executable's auto-start defaults to false -- StoppedByOperator=false should
        // still NOT auto-start (the existing gate). Defends against accidental
        // re-ordering where the new check could overpower the existing one.
        await SeedAppAsync(dbFactory, "tool", "executable", stoppedByOperator: false);

        var (supervisor, runner) = await CreateSupervisorAsync(dbFactory);

        await supervisor.StartAsync(CancellationToken.None);

        runner.StartCallCount.ShouldBe(0);
    }

    // --- Helpers ---

    private static async Task<App> SeedAppAsync
    (
        IDbContextFactory<AppDbContext> dbFactory,
        string slug,
        string appTypeSlug,
        string displayName = "Seed",
        bool stoppedByOperator = false
    )
    {
        await using var context = await dbFactory.CreateDbContextAsync();

        var app = new App
        {
            Slug = slug,
            DisplayName = displayName,
            AppTypeSlug = appTypeSlug,
            StoppedByOperator = stoppedByOperator
        };

        context.Apps.Add(app);
        await context.SaveChangesAsync();

        return app;
    }

    private static async Task<ProxyManager> CreateProxyManagerAsync(IDbContextFactory<AppDbContext> dbFactory)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var appStore = new AppStore(dbFactory, cache, NullLogger<AppStore>.Instance);

        var settings = new ProxySettings
        {
            BaseDomain = "collab.internal",
            BinaryPath = null,
            ListenAddress = ":443",
            CertLifetime = "168h",
            AdminPort = 2019
        };

        var typeStore = new TypeStore
        (
            new EventBus<TypeStoreReloadedEvent>(),
            new TypeStoreSettings { UserTypesDirectory = Path.Combine(Path.GetTempPath(), "collabhost-350-notexist") },
            settings,
            new StubHostEnvironment(),
            NullLogger<TypeStore>.Instance
        );

        await typeStore.LoadAsync();

        var capabilityStore = new CapabilityStore(typeStore, appStore, NullLogger<CapabilityStore>.Instance);
        var eventBus = new EventBus<ProcessStateChangedEvent>();
        var activityEventStore = new ActivityEventStore(dbFactory, NullLogger<ActivityEventStore>.Instance);
        var bundleDirectory = new HostedAppBundleDirectory(Path.GetTempPath(), NullLogger<HostedAppBundleDirectory>.Instance);

        var supervisor = new ProcessSupervisor
        (
            new UnusedRunner(),
            new NullContainment(),
            appStore,
            capabilityStore,
            typeStore,
            eventBus,
            [],
            [],
            bundleDirectory,
            activityEventStore,
            NullLogger<ProcessSupervisor>.Instance
        );

        return new ProxyManager
        (
            new NoOpCaddyClient(),
            appStore,
            capabilityStore,
            typeStore,
            supervisor,
            eventBus,
            settings,
            new HostingSettings { ListenAddress = "localhost", ListenPort = 58400 },
            new Collabhost.Api.Portal.PortalSettings { Subdomain = "collabhost" },
            activityEventStore,
            new Collabhost.Api.StaticSite.RuntimeConfigFileWriter(capabilityStore, NullLogger<Collabhost.Api.StaticSite.RuntimeConfigFileWriter>.Instance),
            TimeProvider.System,
            NullLogger<ProxyManager>.Instance
        );
    }

    private static async Task<(ProcessSupervisor supervisor, IRecordingRunner runner)> CreateSupervisorAsync
    (
        IDbContextFactory<AppDbContext> dbFactory
    )
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var appStore = new AppStore(dbFactory, cache, NullLogger<AppStore>.Instance);

        var settings = new ProxySettings
        {
            BaseDomain = "collab.internal",
            BinaryPath = null,
            ListenAddress = ":443",
            CertLifetime = "168h"
        };

        var typeStore = new TypeStore
        (
            new EventBus<TypeStoreReloadedEvent>(),
            new TypeStoreSettings { UserTypesDirectory = Path.Combine(Path.GetTempPath(), "collabhost-350-supervisor-notexist") },
            settings,
            new StubHostEnvironment(),
            NullLogger<TypeStore>.Instance
        );

        await typeStore.LoadAsync();

        var capabilityStore = new CapabilityStore(typeStore, appStore, NullLogger<CapabilityStore>.Instance);
        var eventBus = new EventBus<ProcessStateChangedEvent>();
        var activityEventStore = new ActivityEventStore(dbFactory, NullLogger<ActivityEventStore>.Instance);
        var bundleDirectory = new HostedAppBundleDirectory(Path.GetTempPath(), NullLogger<HostedAppBundleDirectory>.Instance);

        var runner = new RecordingRunner();

        var supervisor = new ProcessSupervisor
        (
            runner,
            new NullContainment(),
            appStore,
            capabilityStore,
            typeStore,
            eventBus,
            [],
            [],
            bundleDirectory,
            activityEventStore,
            NullLogger<ProcessSupervisor>.Instance
        );

        return (supervisor, runner);
    }

    private static async Task InvokeHydrateAsync(ProxyManager manager)
    {
        var method = typeof(ProxyManager).GetMethod
        (
            "HydrateRouteStatesFromPersistenceAsync",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        method.ShouldNotBeNull();

        var task = (Task)method.Invoke(manager, [CancellationToken.None])!;

        await task;
    }

    private static ConcurrentDictionary<string, bool> GetRouteStates(ProxyManager manager)
    {
        var field = typeof(ProxyManager).GetField
        (
            "_routeStates",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        field.ShouldNotBeNull();

        return (ConcurrentDictionary<string, bool>)field.GetValue(manager)!;
    }
}

file sealed class TestDbContextFactory
(
    SqliteConnection connection
) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => Build(connection);

#pragma warning disable VSTHRD200 // Async method naming -- interface implementation
    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
#pragma warning restore VSTHRD200
        Task.FromResult(Build(connection));

    private static AppDbContext Build(SqliteConnection sharedConnection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(sharedConnection)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new AppDbContext(options);
    }
}

file sealed class NoOpCaddyClient : ICaddyClient
{
    public Task<bool> IsReadyAsync(CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<LoadConfigResult> LoadConfigAsync(System.Text.Json.Nodes.JsonObject config, CancellationToken ct = default) =>
        Task.FromResult(LoadConfigResult.Ok());

    public Task<System.Text.Json.Nodes.JsonObject?> GetConfigAsync(CancellationToken ct = default) =>
        Task.FromResult<System.Text.Json.Nodes.JsonObject?>(null);
}

file sealed class UnusedRunner : IManagedProcessRunner
{
    public IProcessHandle Start(ProcessStartConfiguration configuration) =>
        throw new NotSupportedException("Not used here");

    public Task<ProcessRunResult> RunToCompletionAsync
    (
        ProcessStartConfiguration configuration,
        TimeSpan timeout,
        CancellationToken ct = default
    ) =>
        throw new NotSupportedException("Not used here");
}

file sealed class RecordingRunner : IManagedProcessRunner, IRecordingRunner
{
    public int StartCallCount { get; private set; }

    public IProcessHandle Start(ProcessStartConfiguration configuration)
    {
        StartCallCount++;
        throw new NotSupportedException("StartAsync auto-start path should never reach the runner in these tests");
    }

    public Task<ProcessRunResult> RunToCompletionAsync
    (
        ProcessStartConfiguration configuration,
        TimeSpan timeout,
        CancellationToken ct = default
    ) =>
        throw new NotSupportedException("Not used here");
}
