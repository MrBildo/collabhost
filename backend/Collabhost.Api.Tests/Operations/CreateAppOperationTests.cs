using System.Globalization;
using System.Text.Json.Nodes;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Data;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Events;
using Collabhost.Api.Operations;
using Collabhost.Api.Platform;
using Collabhost.Api.Portal;
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

// Operation-level unit tests for CreateAppOperation (#406 spine PR 6, code-structure-conventions
// §8/§9). These drive operation.ExecuteAsync DIRECTLY against a real AppStore + ActivityEventStore +
// TypeStore + ProxyManager over in-memory SQLite, plus a CurrentUser the test sets so the base's
// actor stamp is exercised. They are the new behavioral coverage the migration adds -- the surface-
// blind core paths the leaf + base uniquely own, driven deterministically without a live process.
//
// What is driven directly here (and why):
//
//   - Conflict (exists-check, Marcus R2/R3): a duplicate slug returns OperationResult.Conflict with
//     the bare "An app with slug '{slug}' already exists." message (the REST message -- the MCP
//     adapter drops its pre-migration "Use list_apps" suffix, the disclosed R2 normalization). Exists
//     is checked BEFORE the type lookup, so a duplicate-slug request reds with Conflict even were the
//     type bad (R3 order preserved). No second app is created and no event recorded.
//
//   - NotFound (type-lookup, Marcus R5): a bogus app-type slug returns OperationResult.NotFound("App
//     type not found.") -- the operation's bare message. The MCP surface maps this NotFound back to
//     the rich AppTypeNotFound(slug) shape via its result mapper (proven at the surface oracle); REST
//     maps it to 404. No app is created.
//
//   - Validation: an invalid field in a section returns OperationResult.Validation with the
//     section-qualified joined errors, and NO app is created (registration is transactional --
//     validation runs before CreateAsync).
//
//   - Success with ONE event (normal): a static-site (routing, no process, no external-target) is
//     created -> Success, the app row exists, exactly ONE app.created event is recorded, the route is
//     left DISABLED (routing-only apps await an artifact directory), and the hosts hint is emitted
//     (the dedup'd ResolveHelpfulNextSteps -- a routed type gets the hint).
//
//   - Success with TWO events (external-route): an external-route (routing + external-target, no
//     process) auto-enables its route at registration (Card #348 D8) and records BOTH app.created AND
//     app.started -- the 1-vs-2-event conditional the migration must preserve. This is the path the
//     operation's hasExternalTarget branch uniquely owns.
//
//   - Hints scoping: a non-routed type (system-service -- no routing binding) gets an EMPTY hint list
//     (no Caddy route, no hosts entry), proving the ResolveHelpfulNextSteps routing gate folded in.
//
// The full HTTP/transport happy path is the dual oracle: RegistrationValidationTests (REST, route-
// level, untouched) + McpToolTests.RegisterApp_WithInstallDirectory_PersistsArtifactLocation (MCP,
// direct-call, untouched) exercise the surface adapters end-to-end.
#pragma warning disable CA1001 // SqliteConnection disposed via IDisposable below
public sealed class CreateAppOperationTests : IDisposable
#pragma warning restore CA1001
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly AppStore _appStore;
    private readonly ActivityEventStore _activityEventStore;
    private readonly CurrentUser _currentUser;
    private readonly User _actor;

    public CreateAppOperationTests()
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

    [Fact]
    public async Task DuplicateSlug_ReturnsConflict_NoSecondApp_NoEvent()
    {
        await SeedAppAsync("dupe", "static-site");

        var (operation, _) = await CreateOperationAsync();

        var result = await operation.ExecuteAsync
        (
            new CreateAppCommand("dupe", "Dupe", "static-site", []),
            CancellationToken.None
        );

        result.IsSuccess.ShouldBeFalse();
        result.FailureKind.ShouldBe(OperationFailureKind.Conflict);
        result.Error.ShouldBe("An app with slug 'dupe' already exists.");

        // Still exactly one row, and no app.created event from this rejected attempt.
        await AssertAppCountAsync(1);
        await AssertNoEventRecordedAsync();
    }

    [Fact]
    public async Task UnknownAppType_ReturnsNotFound_NoApp()
    {
        var (operation, _) = await CreateOperationAsync();

        var result = await operation.ExecuteAsync
        (
            new CreateAppCommand("ghost", "Ghost", "no-such-type", []),
            CancellationToken.None
        );

        result.IsSuccess.ShouldBeFalse();
        result.FailureKind.ShouldBe(OperationFailureKind.NotFound);
        result.Error.ShouldBe("App type not found.");

        await AssertAppCountAsync(0);
        await AssertNoEventRecordedAsync();
    }

    [Fact]
    public async Task InvalidSection_ReturnsValidation_NoApp_NoEvent()
    {
        var (operation, _) = await CreateOperationAsync();

        // An unknown field in the process section -> ValidateEdits returns errors -> Validation,
        // BEFORE the app is created (registration is transactional).
        var overrides = new JsonObject
        {
            ["process"] = new JsonObject { ["notAField"] = "x" }
        };

        var result = await operation.ExecuteAsync
        (
            new CreateAppCommand("bad-cfg", "Bad Config", "executable", overrides),
            CancellationToken.None
        );

        result.IsSuccess.ShouldBeFalse();
        result.FailureKind.ShouldBe(OperationFailureKind.Validation);
        result.Error.ShouldNotBeNullOrEmpty();

        await AssertAppCountAsync(0);
        await AssertNoEventRecordedAsync();
    }

    [Fact]
    public async Task UnknownCapabilitySection_ReturnsValidation_NoApp_NoEvent()
    {
        var (operation, _) = await CreateOperationAsync();

        // #436: a section the resolved app type does not bind ("garbage" is not a capability at
        // all) is rejected at registration, transactionally -- no row, no event. Before the fix
        // CapabilityResolver.ValidateEdits returned zero errors for an unknown section (GetSchema
        // null) so junk persisted silently. The reject message names the offending section.
        var overrides = new JsonObject
        {
            ["garbage"] = new JsonObject { ["foo"] = "bar" }
        };

        var result = await operation.ExecuteAsync
        (
            new CreateAppCommand("junk-cfg", "Junk Config", "static-site", overrides),
            CancellationToken.None
        );

        result.IsSuccess.ShouldBeFalse();
        result.FailureKind.ShouldBe(OperationFailureKind.Validation);
        result.Error.ShouldNotBeNullOrEmpty();
        result.Error!.ShouldContain("garbage");

        await AssertAppCountAsync(0);
        await AssertNoEventRecordedAsync();
    }

    [Fact]
    public async Task CatalogKnownButTypeUnboundSection_ReturnsValidation_NoApp()
    {
        var (operation, _) = await CreateOperationAsync();

        // #436: the membership test is the RESOLVED TYPE'S bindings, not the builtin catalog.
        // `process` is a real catalog capability (its schema is non-null), but static-site does
        // not BIND it -- so a process override on a static-site must reject. This is the property
        // that distinguishes "validate against the type's bindings" from "validate against the
        // catalog"; the latter would wrongly accept this.
        var overrides = new JsonObject
        {
            ["process"] = new JsonObject { ["command"] = "/usr/bin/whatever" }
        };

        var result = await operation.ExecuteAsync
        (
            new CreateAppCommand("unbound-cfg", "Unbound Config", "static-site", overrides),
            CancellationToken.None
        );

        result.IsSuccess.ShouldBeFalse();
        result.FailureKind.ShouldBe(OperationFailureKind.Validation);
        result.Error!.ShouldContain("process");

        await AssertAppCountAsync(0);
    }

    [Fact]
    public async Task StaticSite_Created_RecordsOneEvent_RouteDisabled_EmitsHint()
    {
        var (operation, proxy) = await CreateOperationAsync();

        var result = await operation.ExecuteAsync
        (
            new CreateAppCommand("site-a", "Site A", "static-site", []),
            CancellationToken.None
        );

        result.IsSuccess.ShouldBeTrue();
        result.Value!.Slug.ShouldBe("site-a");
        result.Value.Id.ShouldNotBe(default);

        // Routed type -> the hosts hint is emitted (ResolveHelpfulNextSteps dedup'd into the operation).
        result.Value.Hints.ShouldNotBeEmpty();
        result.Value.Hints[0].ShouldContain("site-a");

        // Exactly ONE app.created event (no external-target -> no second app.started event).
        await AssertSingleEventAsync(ActivityEventTypes.AppCreated, "site-a");

        // Routing-only app starts with its route DISABLED (awaits an artifact directory).
        proxy.IsRouteExplicitlyEnabled("site-a").ShouldBeFalse();

        var created = await _appStore.GetBySlugAsync("site-a", CancellationToken.None);
        created.ShouldNotBeNull();
        created!.AppTypeSlug.ShouldBe("static-site");
    }

    [Fact]
    public async Task ExternalRoute_Created_RecordsTwoEvents_RouteEnabled()
    {
        var (operation, proxy) = await CreateOperationAsync();

        // A valid external-target override (localhost passes with AllowPublicHosts = false).
        var overrides = new JsonObject
        {
            ["external-target"] = new JsonObject
            {
                ["host"] = "localhost",
                ["port"] = 8080,
                ["scheme"] = "http"
            }
        };

        var result = await operation.ExecuteAsync
        (
            new CreateAppCommand("ext-a", "Ext A", "external-route", overrides),
            CancellationToken.None
        );

        result.IsSuccess.ShouldBeTrue();
        result.Value!.Slug.ShouldBe("ext-a");

        // The 1-vs-2-event conditional: external-route records BOTH app.created AND app.started.
        await AssertEventTypesAsync("ext-a", ActivityEventTypes.AppCreated, ActivityEventTypes.AppStarted);

        // External-route auto-enables its route at registration (Card #348 D8). Order matters: the
        // route is enabled BEFORE either event is recorded, but EnableRoute itself only flips in-memory
        // state, so the assertion below reads the live state after the operation completes.
        proxy.IsRouteExplicitlyEnabled("ext-a").ShouldBeTrue();
    }

    [Fact]
    public async Task SystemService_Created_EmitsNoHint()
    {
        var (operation, _) = await CreateOperationAsync();

        var result = await operation.ExecuteAsync
        (
            new CreateAppCommand("svc-a", "Svc A", "system-service", []),
            CancellationToken.None
        );

        result.IsSuccess.ShouldBeTrue();

        // system-service has no routing binding -> no Caddy route -> no hosts hint.
        result.Value!.Hints.ShouldBeEmpty();
    }

    // --- Helpers ---

    private async Task<(CreateAppOperation Operation, ProxyManager Proxy)> CreateOperationAsync()
    {
        var (typeStore, capabilityStore) = await CreateStoresAsync();
        var supervisor = CreateSupervisor(typeStore, capabilityStore);
        var proxy = CreateProxyManager(supervisor, typeStore, capabilityStore);

        var proxySettings = new ProxySettings
        {
            BaseDomain = "collab.internal",
            BinaryPath = null,
            ListenAddress = ":443",
            CertLifetime = "168h"
        };

        var operation = new CreateAppOperation
        (
            _appStore,
            typeStore,
            proxy,
            proxySettings,
            new ExternalTargetSettings { AllowPublicHosts = false },
            _currentUser,
            _activityEventStore
        );

        return (operation, proxy);
    }

    private async Task<App> SeedAppAsync(string slug, string appTypeSlug)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();

        var app = new App
        {
            Slug = slug,
            DisplayName = "Seed",
            AppTypeSlug = appTypeSlug
        };

        context.Apps.Add(app);
        await context.SaveChangesAsync();

        return app;
    }

    private async Task<(TypeStore TypeStore, CapabilityStore CapabilityStore)> CreateStoresAsync()
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
            new TypeStoreSettings { UserTypesDirectory = Path.Combine(Path.GetTempPath(), "collabhost-406pr6-notexist") },
            settings,
            new StubHostEnvironment(),
            NullLogger<TypeStore>.Instance
        );

        await typeStore.LoadAsync();

        var capabilityStore = new CapabilityStore(typeStore, _appStore, NullLogger<CapabilityStore>.Instance);

        return (typeStore, capabilityStore);
    }

    private ProcessSupervisor CreateSupervisor(TypeStore typeStore, CapabilityStore capabilityStore)
    {
        var bundleDirectory = new HostedAppBundleDirectory(Path.GetTempPath(), NullLogger<HostedAppBundleDirectory>.Instance);

        return new ProcessSupervisor
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
            new HostingSettings { ListenAddress = "localhost", ListenPort = 58500 },
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

    private async Task AssertAppCountAsync(int expected)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        (await db.Apps.CountAsync()).ShouldBe(expected);
    }

    private async Task AssertNoEventRecordedAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        (await db.ActivityEvents.CountAsync()).ShouldBe(0);
    }

    private async Task AssertSingleEventAsync(string eventType, string slug)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var recorded = await db.ActivityEvents.SingleAsync();

        recorded.EventType.ShouldBe(eventType);
        recorded.AppSlug.ShouldBe(slug);
        recorded.ActorId.ShouldBe(_actor.Id.ToString(null, CultureInfo.InvariantCulture));
        recorded.ActorName.ShouldBe(_actor.Name);
    }

    // Assert the recorded events for a slug are exactly the expected set (count + types), order-
    // independent: both events are recorded in the same operation in rapid succession, so a wall-clock
    // Timestamp ordering would be flaky. The conditional under test is presence-and-count (one event
    // for a normal app, two for an external-route), which this asserts precisely.
    private async Task AssertEventTypesAsync(string slug, params string[] expectedEventTypes)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var recorded = await db.ActivityEvents
            .Where(e => e.AppSlug == slug)
                .Select(e => e.EventType)
                .ToListAsync();

        recorded.Count.ShouldBe(expectedEventTypes.Length);
        recorded.ShouldBe(expectedEventTypes, ignoreOrder: true);
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

    public Task<LoadConfigResult> LoadConfigAsync(JsonObject config, CancellationToken ct = default) =>
        Task.FromResult(LoadConfigResult.Ok());

    public Task<JsonObject?> GetConfigAsync(CancellationToken ct = default) =>
        Task.FromResult<JsonObject?>(null);
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
