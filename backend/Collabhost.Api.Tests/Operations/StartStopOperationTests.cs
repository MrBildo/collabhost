using System.Globalization;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Data;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Events;
using Collabhost.Api.Operations;
using Collabhost.Api.Platform;
using Collabhost.Api.Portal;
using Collabhost.Api.Probes;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;
using Collabhost.Api.StaticSite;
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

namespace Collabhost.Api.Tests.Operations;

// Operation-level unit tests for StartAppOperation and StopAppOperation (#406 spine PR 3,
// code-structure-conventions §8/§9). These drive operation.ExecuteAsync DIRECTLY -- the new
// behavioral coverage the dual-branch migration adds -- against a real AppStore +
// ActivityEventStore + ProcessSupervisor + ProxyManager + ProbeService over in-memory SQLite, plus
// a CurrentUser the test sets so the base's actor stamp is exercised.
//
// What is driven directly here (and why):
//
//   - NotFound: the leaf's explicit OperationResult.NotFound when the slug is absent. The
//     supervisor/proxy are never touched and no activity event is recorded.
//
//   - The ROUTING-ONLY branch (a static-site -- has routing, no process) end-to-end. This is the
//     half of the dual branch the operation can drive DETERMINISTICALLY without a live process,
//     and it is the heart of PR 3: route toggle + the Card #350 persist-flag write-through + the
//     Card #366 probe refresh + the actor-stamped event, plus the success-outcome shape. Start
//     enables the route, clears StoppedByOperator, refreshes the probe cache (NeverProbed -> Fresh
//     for a static-site with a real artifact), records app.started, and returns Running; Stop
//     disables the route, sets StoppedByOperator, records app.stopped, and returns Stopped.
//
// The PROCESS branch's live success path is NOT fabricated here (a real running process needs the
// supervisor's full start fixture, whose mechanics its own tests own) -- it stays covered by the
// existing route-level REST integration tests and the MCP transport tests (the dual oracle). The
// routing-only branch is the operation's uniquely-owned deterministic surface, so it is what these
// pin.
#pragma warning disable CA1001 // SqliteConnection disposed via IDisposable below
public sealed class StartStopOperationTests : IDisposable
#pragma warning restore CA1001
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly AppStore _appStore;
    private readonly ActivityEventStore _activityEventStore;
    private readonly CurrentUser _currentUser;
    private readonly User _actor;

    public StartStopOperationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _dbFactory = new TestDbContextFactory(_connection);

        using (var db = _dbFactory.CreateDbContext())
        {
            db.Database.EnsureCreated();
        }

        _appStore = new AppStore(_dbFactory, new MemoryCache(new MemoryCacheOptions()), NullLogger<AppStore>.Instance);
        _activityEventStore = new ActivityEventStore(_dbFactory, NullLogger<ActivityEventStore>.Instance);

        _actor = new User
        {
            Name = "Test Actor",
            AuthKey = Ulid.NewUlid().ToString(null, CultureInfo.InvariantCulture),
            Role = UserRole.Administrator
        };

        _currentUser = new CurrentUser();
        _currentUser.Set(_actor);
    }

    // --- StartAppOperation ---

    [Fact]
    public async Task Start_UnknownSlug_ReturnsNotFoundAndRecordsNoEvent()
    {
        var (operation, _) = await CreateStartOperationAsync();

        var result = await operation.ExecuteAsync(new StartAppCommand("no-such-app"), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.FailureKind.ShouldBe(OperationFailureKind.NotFound);
        result.Error.ShouldBe("App 'no-such-app' not found.");

        await AssertNoEventRecordedAsync();
    }

    [Fact]
    public async Task Start_RoutingOnly_EnablesRoute_ClearsStoppedFlag_RecordsStarted_ReturnsRunning()
    {
        // A static-site has routing + no process. Start must enable the route, clear the persisted
        // operator-stop flag (Card #350), refresh the probe cache (Card #366), record app.started,
        // and return Running -- byte-identical to both surfaces' pre-migration routing-only branch.
        var app = await SeedAppAsync("site-a", "static-site", stoppedByOperator: true);

        var (operation, proxy) = await CreateStartOperationAsync();

        var result = await operation.ExecuteAsync(new StartAppCommand("site-a"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();

        var outcome = result.Value!;
        outcome.Slug.ShouldBe("site-a");
        outcome.AppTypeSlug.ShouldBe("static-site");
        outcome.State.ShouldBe(ProcessState.Running);
        outcome.HasProcess.ShouldBeFalse();
        outcome.HasRouting.ShouldBeTrue();

        proxy.IsRouteEnabled("site-a").ShouldBeTrue();

        var refreshed = await _appStore.GetByIdAsync(app.Id, CancellationToken.None);
        refreshed.ShouldNotBeNull();
        refreshed.StoppedByOperator.ShouldBeFalse();

        await AssertEventRecordedAsync(ActivityEventTypes.AppStarted, "site-a");
    }

    // --- StopAppOperation ---

    [Fact]
    public async Task Stop_UnknownSlug_ReturnsNotFoundAndRecordsNoEvent()
    {
        var (operation, _) = await CreateStopOperationAsync();

        var result = await operation.ExecuteAsync(new StopAppCommand("no-such-app"), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.FailureKind.ShouldBe(OperationFailureKind.NotFound);
        result.Error.ShouldBe("App 'no-such-app' not found.");

        await AssertNoEventRecordedAsync();
    }

    [Fact]
    public async Task Stop_RoutingOnly_DisablesRoute_SetsStoppedFlag_RecordsStopped_ReturnsStopped()
    {
        // A static-site Stop must disable the route, persist the operator-stop intent (Card #350),
        // record app.stopped, and return Stopped -- byte-identical to both surfaces' pre-migration
        // routing-only branch.
        var app = await SeedAppAsync("site-b", "static-site", stoppedByOperator: false);

        var (operation, proxy) = await CreateStopOperationAsync();

        var result = await operation.ExecuteAsync(new StopAppCommand("site-b"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();

        var outcome = result.Value!;
        outcome.Slug.ShouldBe("site-b");
        outcome.AppTypeSlug.ShouldBe("static-site");
        outcome.State.ShouldBe(ProcessState.Stopped);
        outcome.HasProcess.ShouldBeFalse();
        outcome.HasRouting.ShouldBeTrue();

        proxy.IsRouteEnabled("site-b").ShouldBeFalse();

        var refreshed = await _appStore.GetByIdAsync(app.Id, CancellationToken.None);
        refreshed.ShouldNotBeNull();
        refreshed.StoppedByOperator.ShouldBeTrue();

        await AssertEventRecordedAsync(ActivityEventTypes.AppStopped, "site-b");
    }

    // --- Helpers ---

    private async Task<(StartAppOperation Operation, ProxyManager Proxy)> CreateStartOperationAsync()
    {
        var (supervisor, typeStore, capabilityStore) = await CreateSupervisorAsync();
        var proxy = CreateProxyManager(supervisor, typeStore, capabilityStore);
        var probeService = new ProbeService(_appStore, capabilityStore, TimeProvider.System, NullLogger<ProbeService>.Instance);
        var writer = new RuntimeConfigFileWriter
        (
            capabilityStore,
            new AppDataPathResolver(Path.GetTempPath()),
            NullLogger<RuntimeConfigFileWriter>.Instance
        );

        var operation = new StartAppOperation
        (
            _appStore,
            typeStore,
            supervisor,
            proxy,
            probeService,
            writer,
            _currentUser,
            _activityEventStore
        );

        return (operation, proxy);
    }

    private async Task<(StopAppOperation Operation, ProxyManager Proxy)> CreateStopOperationAsync()
    {
        var (supervisor, typeStore, capabilityStore) = await CreateSupervisorAsync();
        var proxy = CreateProxyManager(supervisor, typeStore, capabilityStore);

        var operation = new StopAppOperation
        (
            _appStore,
            typeStore,
            supervisor,
            proxy,
            _currentUser,
            _activityEventStore
        );

        return (operation, proxy);
    }

    private async Task<App> SeedAppAsync(string slug, string appTypeSlug, bool stoppedByOperator = false)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();

        var app = new App
        {
            Slug = slug,
            DisplayName = "Seed",
            AppTypeSlug = appTypeSlug,
            StoppedByOperator = stoppedByOperator
        };

        context.Apps.Add(app);
        await context.SaveChangesAsync();

        return app;
    }

    private async Task<(ProcessSupervisor Supervisor, TypeStore TypeStore, CapabilityStore CapabilityStore)> CreateSupervisorAsync()
    {
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
            new TypeStoreSettings { UserTypesDirectory = Path.Combine(Path.GetTempPath(), "collabhost-406pr3-notexist") },
            settings,
            new StubHostEnvironment(),
            NullLogger<TypeStore>.Instance
        );

        await typeStore.LoadAsync();

        var capabilityStore = new CapabilityStore(typeStore, _appStore, NullLogger<CapabilityStore>.Instance);
        var bundleDirectory = new HostedAppBundleDirectory(Path.GetTempPath(), NullLogger<HostedAppBundleDirectory>.Instance);

        var supervisor = new ProcessSupervisor
        (
            new UnusedRunner(),
            new NullContainment(),
            _appStore,
            capabilityStore,
            typeStore,
            new EventBus<ProcessStateChangedEvent>(),
            [],
            [],
            [],
            bundleDirectory,
            new PortAllocator(),
            _activityEventStore,
            NullLogger<ProcessSupervisor>.Instance
        );

        return (supervisor, typeStore, capabilityStore);
    }

    private ProxyManager CreateProxyManager
    (
        ProcessSupervisor supervisor,
        TypeStore typeStore,
        CapabilityStore capabilityStore
    )
    {
        var settings = new ProxySettings
        {
            BaseDomain = "collab.internal",
            BinaryPath = null,
            ListenAddress = ":443",
            CertLifetime = "168h",
            AdminPort = 2019
        };

        return new ProxyManager
        (
            new NoOpCaddyClient(),
            _appStore,
            capabilityStore,
            typeStore,
            supervisor,
            new EventBus<ProcessStateChangedEvent>(),
            settings,
            new HostingSettings { ListenAddress = "localhost", ListenPort = 58400 },
            new PortalSettings { Subdomain = "collabhost" },
            _activityEventStore,
            new RuntimeConfigFileWriter
            (
                capabilityStore,
                new AppDataPathResolver(Path.GetTempPath()),
                NullLogger<RuntimeConfigFileWriter>.Instance
            ),
            new AppDataPathResolver(Path.GetTempPath()),
            TimeProvider.System,
            NullLogger<ProxyManager>.Instance
        );
    }

    private async Task AssertNoEventRecordedAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        (await db.ActivityEvents.CountAsync()).ShouldBe(0);
    }

    private async Task AssertEventRecordedAsync(string eventType, string slug)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var recorded = await db.ActivityEvents.SingleAsync();

        recorded.EventType.ShouldBe(eventType);
        recorded.AppSlug.ShouldBe(slug);
        recorded.ActorId.ShouldBe(_actor.Id.ToString(null, CultureInfo.InvariantCulture));
        recorded.ActorName.ShouldBe(_actor.Name);
    }

    public void Dispose() => _connection.Dispose();
}

file sealed class TestDbContextFactory(SqliteConnection connection) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => Build(connection);

#pragma warning disable VSTHRD200 // Async naming -- interface implementation
    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
#pragma warning restore VSTHRD200
        Task.FromResult(Build(connection));

    private static AppDbContext Build(SqliteConnection sharedConnection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(sharedConnection)
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
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
        throw new NotSupportedException("No process should be started in these tests.");

    public Task<ProcessRunResult> RunToCompletionAsync
    (
        ProcessStartConfiguration configuration,
        TimeSpan timeout,
        CancellationToken ct = default
    ) =>
        throw new NotSupportedException("No process should be run in these tests.");
}
