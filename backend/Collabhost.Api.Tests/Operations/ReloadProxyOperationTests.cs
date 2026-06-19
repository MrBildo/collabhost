using System.Globalization;
using System.Text.Json.Nodes;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Data;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Events;
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

// Operation-level unit tests for ReloadProxyOperation (#406 spine PR 4, code-structure-conventions
// §8/§9). These drive operation.ExecuteAsync DIRECTLY -- the new behavioral coverage the migration
// adds -- against a real ProxyManager + ActivityEventStore over in-memory SQLite, plus a CurrentUser
// the test sets so the base's actor stamp is exercised.
//
// ReloadProxy is the trivial, app-less operation: no app lookup, no branch, no failure path
// (RequestSync only enqueues a channel write and never throws; the leaf returns Success
// unconditionally). So its uniquely-owned surface is exactly two things, both pinned here:
//
//   - The Success outcome (an empty ProxyReloadOutcome marker -- the operation has no per-app data
//     to return; both surfaces map the Success arm without reading a value).
//   - The app-less event record via the base's RecordAsync(eventType, ct) overload -- proxy.reloaded
//     stamped with the acting user and carrying NULL AppId/AppSlug (the first app-less event the
//     spine records). This is the path that distinguishes this op from every lifecycle op, whose
//     events carry an app.
#pragma warning disable CA1001 // SqliteConnection disposed via IDisposable below
public sealed class ReloadProxyOperationTests : IDisposable
#pragma warning restore CA1001
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ActivityEventStore _activityEventStore;
    private readonly CurrentUser _currentUser;
    private readonly User _actor;

    public ReloadProxyOperationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _dbFactory = new InMemoryDbContextFactory(_connection);

        using (var db = _dbFactory.CreateDbContext())
        {
            db.Database.EnsureCreated();
        }

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
    public async Task Reload_ReturnsSuccessWithEmptyOutcome()
    {
        var operation = CreateOperation();

        var result = await operation.ExecuteAsync(new ReloadProxyCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBeNull();
    }

    [Fact]
    public async Task Reload_RecordsAppLessProxyReloadedEventStampedWithActor()
    {
        var operation = CreateOperation();

        await operation.ExecuteAsync(new ReloadProxyCommand(), CancellationToken.None);

        var recorded = await SingleRecordedEventAsync();

        recorded.EventType.ShouldBe(ActivityEventTypes.ProxyReloaded);
        recorded.ActorId.ShouldBe(_actor.Id.ToString(null, CultureInfo.InvariantCulture));
        recorded.ActorName.ShouldBe("Test Actor");

        // The defining trait of the app-less op: the event carries no app (the base's app-less
        // RecordAsync overload passes null AppId/AppSlug), unlike every lifecycle op's event.
        recorded.AppId.ShouldBeNull();
        recorded.AppSlug.ShouldBeNull();
        recorded.MetadataJson.ShouldBeNull();
    }

    private ReloadProxyOperation CreateOperation() =>
        new(CreateProxyManager(), _currentUser, _activityEventStore);

    // A real ProxyManager built with minimal real dependencies (StartAsync is never called, so the
    // background sync loop is not running; RequestSync only enqueues a channel write). Mirrors
    // ProxyManagerTests.CreateProxyManager so the operation exercises the production RequestSync
    // path, not a fake.
    private ProxyManager CreateProxyManager()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var appStore = new AppStore(_dbFactory, cache, NullLogger<AppStore>.Instance);

        var proxySettings = new ProxySettings
        {
            BaseDomain = "collab.internal",
            BinaryPath = "caddy",
            ListenAddress = ":443",
            CertLifetime = "168h",
            AdminPort = 2019
        };

        var typeStore = new TypeStore
        (
            new EventBus<TypeStoreReloadedEvent>(),
            new TypeStoreSettings { UserTypesDirectory = Path.Combine(Path.GetTempPath(), "collabhost-406-pr4-notexist") },
            proxySettings,
            new StubHostEnvironment(),
            NullLogger<TypeStore>.Instance
        );

        var capabilityStore = new CapabilityStore(typeStore, appStore, NullLogger<CapabilityStore>.Instance);
        var eventBus = new EventBus<ProcessStateChangedEvent>();
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
            [],
            bundleDirectory,
            new PortAllocator(),
            _activityEventStore,
            NullLogger<ProcessSupervisor>.Instance
        );

        var pathResolver = new AppDataPathResolver(Path.GetTempPath());

        return new ProxyManager
        (
            new ReloadStubCaddyClient(),
            appStore,
            capabilityStore,
            typeStore,
            supervisor,
            eventBus,
            proxySettings,
            new HostingSettings { ListenAddress = "localhost", ListenPort = 58400 },
            new PortalSettings { Subdomain = "collabhost" },
            _activityEventStore,
            new RuntimeConfigFileWriter(capabilityStore, pathResolver, NullLogger<RuntimeConfigFileWriter>.Instance),
            pathResolver,
            TimeProvider.System,
            NullLogger<ProxyManager>.Instance
        );
    }

    private async Task<ActivityEvent> SingleRecordedEventAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        return await db.ActivityEvents.SingleAsync();
    }

    public void Dispose() => _connection.Dispose();
}

file sealed class InMemoryDbContextFactory(SqliteConnection connection) : IDbContextFactory<AppDbContext>
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

file sealed class ReloadStubCaddyClient : ICaddyClient
{
    public Task<bool> IsReadyAsync(CancellationToken ct = default) => Task.FromResult(true);

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
