using System.Globalization;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Data;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Events;
using Collabhost.Api.Operations;
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

namespace Collabhost.Api.Tests.Operations;

// Operation-level unit tests for RestartAppOperation and KillAppOperation (the #406 spine PR 2,
// code-structure-conventions §8/§9). These drive operation.ExecuteAsync DIRECTLY -- the new
// behavioral coverage the migration adds -- against a real AppStore + ActivityEventStore +
// ProcessSupervisor over in-memory SQLite, plus a CurrentUser the test sets so the base's actor
// stamp is exercised. They cover the paths the operation leaf + Operation<,> base uniquely own and
// can drive deterministically without a live process:
//
//   - NotFound: the leaf's explicit OperationResult.NotFound when the slug is absent. The
//     supervisor is never touched and no activity event is recorded.
//   - Conflict via the base hoist: the supervisor throws InvalidOperationException on a process-
//     less app (no managed process to restart/kill); the Operation<,> base catches it and returns
//     OperationResult.Conflict with the supervisor's message, and -- because the record call sits
//     AFTER the supervisor call in the leaf body -- no activity event is recorded.
//
// The success-shape + event-recording on a LIVE process is proven at two adjacent layers rather
// than fabricated here (a real running process needs the supervisor's full start fixture, whose
// mechanics its own tests own): OperationBaseTests proves RecordAsync's actor stamp and the Success
// pass-through, and McpToolTests proves the surface adapter maps the result back to the exact MCP
// shape. The leaf's own success path is straight-line outcome field-copying with no branch.
#pragma warning disable CA1001 // SqliteConnection disposed via IDisposable below
public sealed class RestartKillOperationTests : IDisposable
#pragma warning restore CA1001
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly AppStore _appStore;
    private readonly ActivityEventStore _activityEventStore;
    private readonly CurrentUser _currentUser;
    private readonly User _actor;

    public RestartKillOperationTests()
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

    // --- RestartAppOperation ---

    [Fact]
    public async Task Restart_UnknownSlug_ReturnsNotFoundAndRecordsNoEvent()
    {
        var operation = await CreateRestartOperationAsync();

        var result = await operation.ExecuteAsync(new RestartAppCommand("no-such-app"), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.FailureKind.ShouldBe(OperationFailureKind.NotFound);
        result.Error.ShouldBe("App 'no-such-app' not found.");

        await AssertNoEventRecordedAsync();
    }

    [Fact]
    public async Task Restart_ProcesslessApp_SupervisorThrowsMappedToConflictByBase()
    {
        // A static-site has no managed process; RestartAppAsync -> StartAppInternalAsync throws
        // InvalidOperationException, which the Operation<,> base catches and maps to Conflict. This
        // exercises the base's try/catch-to-Conflict hoist end-to-end through a real supervisor.
        await SeedAppAsync("site-a", "static-site");

        var operation = await CreateRestartOperationAsync();

        var result = await operation.ExecuteAsync(new RestartAppCommand("site-a"), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.FailureKind.ShouldBe(OperationFailureKind.Conflict);
        result.Error.ShouldNotBeNullOrEmpty();

        // The record call sits after the supervisor call in the leaf body, so a thrown supervisor
        // means no app.restarted event was persisted.
        await AssertNoEventRecordedAsync();
    }

    // --- KillAppOperation ---

    [Fact]
    public async Task Kill_UnknownSlug_ReturnsNotFoundAndRecordsNoEvent()
    {
        var operation = await CreateKillOperationAsync();

        var result = await operation.ExecuteAsync(new KillAppCommand("no-such-app"), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.FailureKind.ShouldBe(OperationFailureKind.NotFound);
        result.Error.ShouldBe("App 'no-such-app' not found.");

        await AssertNoEventRecordedAsync();
    }

    [Fact]
    public async Task Kill_NoManagedProcess_SupervisorThrowsMappedToConflictByBase()
    {
        // KillAppAsync throws InvalidOperationException("No managed process found for this app.")
        // when the app has no tracked process; the base maps it to Conflict carrying that message.
        await SeedAppAsync("api-a", "dotnet-app");

        var operation = await CreateKillOperationAsync();

        var result = await operation.ExecuteAsync(new KillAppCommand("api-a"), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.FailureKind.ShouldBe(OperationFailureKind.Conflict);
        result.Error.ShouldBe("No managed process found for this app.");

        await AssertNoEventRecordedAsync();
    }

    // --- Helpers ---

    private async Task<RestartAppOperation> CreateRestartOperationAsync()
    {
        var (supervisor, typeStore) = await CreateSupervisorAsync();

        return new RestartAppOperation(_appStore, typeStore, supervisor, _currentUser, _activityEventStore);
    }

    private async Task<KillAppOperation> CreateKillOperationAsync()
    {
        var (supervisor, typeStore) = await CreateSupervisorAsync();

        return new KillAppOperation(_appStore, typeStore, supervisor, _currentUser, _activityEventStore);
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

    private async Task<(ProcessSupervisor Supervisor, TypeStore TypeStore)> CreateSupervisorAsync()
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
            new TypeStoreSettings { UserTypesDirectory = Path.Combine(Path.GetTempPath(), "collabhost-406-notexist") },
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

        return (supervisor, typeStore);
    }

    private async Task AssertNoEventRecordedAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        (await db.ActivityEvents.CountAsync()).ShouldBe(0);
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
