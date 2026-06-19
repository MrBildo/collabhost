using System.Globalization;
using System.Text.Json;
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

// Operation-level unit tests for DeleteAppOperation (#406 spine PR 7, the keystone op,
// code-structure-conventions §8/§9). These drive operation.ExecuteAsync DIRECTLY -- the new
// behavioral coverage the migration adds -- against a real AppStore + ActivityEventStore +
// ProcessSupervisor + ProxyManager + ProbeService over in-memory SQLite, plus a CurrentUser the
// test sets so the base's actor stamp is exercised.
//
// What is driven directly here (and why):
//
//   - NotFound: the leaf's explicit OperationResult.NotFound when the slug is absent. The
//     supervisor/proxy/probe-service are never touched and no activity event is recorded.
//
//   - The ROUTING-ONLY delete end-to-end (a static-site -- has routing, no process, not running).
//     This is the half of the delete the operation can drive DETERMINISTICALLY without a live
//     process, and it covers the heart of the migration: the row is deleted, the routing-only route
//     is disabled before the row goes (Card #348), the probe cache is invalidated (Card #337 -- THE
//     #406 parity-fix: MCP delete now does this too, by sharing this one operation), the app.deleted
//     event is recorded actor-stamped with display-name metadata via the base recorder, and the
//     outcome carries slug + display name (the MCP success message needs both). The probe-cache
//     invalidation is proven via the cache transition Fresh -> NeverProbed -- the same cache oracle
//     ProbeTriggerTests uses, run in reverse for delete.
//
// The stop-then-delete path on a LIVE running process is NOT fabricated here (a real running process
// needs the supervisor's full start fixture, whose mechanics its own tests own) -- the stop-if-running
// block is byte-preserved from both pre-migration surfaces and covered by the route-level integration
// tests. The not-running routing-only delete is the operation's uniquely-owned deterministic surface,
// so it is what these pin. The MCP-surface parity (MCP delete invalidating the cache, the one behavior
// change) is additionally locked at the surface in McpToolTests, per the F-1 regime-3 discipline (the
// op-level test cannot see WHICH surface reaches the operation).
#pragma warning disable CA1001 // SqliteConnection disposed via IDisposable below
public sealed class DeleteAppOperationTests : IDisposable
#pragma warning restore CA1001
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly AppStore _appStore;
    private readonly ActivityEventStore _activityEventStore;
    private readonly CurrentUser _currentUser;
    private readonly User _actor;

    public DeleteAppOperationTests()
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
    public async Task Delete_UnknownSlug_ReturnsNotFoundAndRecordsNoEvent()
    {
        var (operation, _, _) = await CreateOperationAsync();

        var result = await operation.ExecuteAsync(new DeleteAppCommand("no-such-app"), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.FailureKind.ShouldBe(OperationFailureKind.NotFound);
        result.Error.ShouldBe("App 'no-such-app' not found.");

        await AssertNoEventRecordedAsync();
    }

    [Fact]
    public async Task Delete_RoutingOnly_RemovesRow_DisablesRoute_InvalidatesProbeCache_RecordsDeleted()
    {
        // A not-running static-site (routing, no process). Delete must remove the row, disable the
        // route before the row goes (Card #348), invalidate the probe cache (Card #337 -- the #406
        // parity behavior shared by both surfaces now), record app.deleted with display-name
        // metadata, and return an outcome carrying slug + display name.
        var artifactDirectory = CreateArtifactDirectory();
        var app = await SeedStaticSiteWithArtifactAsync("site-del", artifactDirectory);

        var (operation, proxy, probeService) = await CreateOperationAsync();

        // Enable the route + prime the probe cache so the delete has something to disable / invalidate.
        proxy.EnableRoute("site-del");
        await probeService.RunProbesAsync(app.Id, CancellationToken.None);
        probeService.GetCachedProbes(app.Id, "static-site").Status.ShouldBe(ProbeCacheStatus.Fresh);

        var result = await operation.ExecuteAsync(new DeleteAppCommand("site-del"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();

        var outcome = result.Value!;
        outcome.Slug.ShouldBe("site-del");
        outcome.DisplayName.ShouldBe("Seed Display");

        // Row removed.
        (await _appStore.GetByIdAsync(app.Id, CancellationToken.None)).ShouldBeNull();

        // Route disabled (routing-only Card #348 path).
        proxy.IsRouteEnabled("site-del").ShouldBeFalse();

        // Probe cache invalidated -- Fresh -> NeverProbed. THE #406 parity behavior (REST always did
        // this; MCP did not until both surfaces shared this operation).
        probeService.GetCachedProbes(app.Id, "static-site").Status.ShouldBe(ProbeCacheStatus.NeverProbed);

        // app.deleted recorded, actor-stamped, with display-name metadata (the row is gone, so the
        // base's string-id recorder overload carries the captured id + slug + metadata).
        await AssertDeletedEventRecordedAsync("site-del");
    }

    // --- Helpers ---

    private async Task<(DeleteAppOperation Operation, ProxyManager Proxy, ProbeService ProbeService)> CreateOperationAsync()
    {
        var (supervisor, typeStore, capabilityStore) = await CreateSupervisorAsync();
        var proxy = CreateProxyManager(supervisor, typeStore, capabilityStore);
        var probeService = new ProbeService(_appStore, capabilityStore, TimeProvider.System, NullLogger<ProbeService>.Instance);

        var operation = new DeleteAppOperation
        (
            _appStore,
            typeStore,
            supervisor,
            proxy,
            probeService,
            _currentUser,
            _activityEventStore
        );

        return (operation, proxy, probeService);
    }

    private static string CreateArtifactDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "collabhost-406pr7-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        // A real entry-point file so the static-site probe yields a content-bearing (not just
        // empty-but-fresh) cache entry, mirroring ProbeTriggerTests.
        File.WriteAllText
        (
            Path.Combine(directory, "index.html"),
            "<!doctype html><title>delete-op-test</title>"
        );

        return directory;
    }

    private async Task<App> SeedStaticSiteWithArtifactAsync(string slug, string artifactDirectory)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();

        var app = new App
        {
            Slug = slug,
            DisplayName = "Seed Display",
            AppTypeSlug = "static-site"
        };

        context.Apps.Add(app);
        await context.SaveChangesAsync();

        // An artifact-location override so ProbeService.RunProbesAsync resolves a real Location and
        // populates the cache (without it, RunProbesAsync returns early on an empty Location).
        context.CapabilityOverrides.Add
        (
            new CapabilityOverride
            {
                AppId = app.Id,
                CapabilitySlug = "artifact",
                ConfigurationJson = $$"""{"location":{{JsonSerializer.Serialize(artifactDirectory)}}}"""
            }
        );

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
            new TypeStoreSettings { UserTypesDirectory = Path.Combine(Path.GetTempPath(), "collabhost-406pr7-notexist") },
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

    private async Task AssertDeletedEventRecordedAsync(string slug)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var recorded = await db.ActivityEvents.SingleAsync();

        recorded.EventType.ShouldBe(ActivityEventTypes.AppDeleted);
        recorded.AppSlug.ShouldBe(slug);
        recorded.ActorId.ShouldBe(_actor.Id.ToString(null, CultureInfo.InvariantCulture));
        recorded.ActorName.ShouldBe(_actor.Name);
        recorded.MetadataJson.ShouldNotBeNull();
        recorded.MetadataJson.ShouldContain("Seed Display");
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
