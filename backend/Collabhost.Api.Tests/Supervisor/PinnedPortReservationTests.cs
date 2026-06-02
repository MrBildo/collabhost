using System.Text.Json;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Data;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Events;
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

namespace Collabhost.Api.Tests.Supervisor;

// Behavioral coverage for the fixed-port pin + reservation (card #373). Exercises
// the production ProcessSupervisor.StartAsync boot path against a real in-memory
// DB and the real built-in AppType bindings, so the reservation that protects a
// pinned port from automatic allocation is proven end-to-end -- not just in the
// PortAllocator unit.
#pragma warning disable CA1001 // SqliteConnection owned + disposed via IAsyncLifetime, mirrors sibling supervisor tests.
public class PinnedPortReservationTests : IAsyncLifetime
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

    [Fact]
    public async Task StartAsync_PinnedApp_ReservesTheFixedPort()
    {
        var dbFactory = new TestDbContextFactory(_connection);
        await SeedPinnedExecutableAsync(dbFactory, "searxng", fixedPort: 8888);

        var (supervisor, allocator) = await CreateSupervisorAsync(dbFactory);

        await supervisor.StartAsync(CancellationToken.None);

        allocator.IsReserved(8888).ShouldBeTrue();
    }

    [Fact]
    public async Task StartAsync_UnpinnedApp_ReservesNothing()
    {
        var dbFactory = new TestDbContextFactory(_connection);
        // An executable app with no fixedPort override -- the historical
        // dynamic-allocation shape. Must reserve nothing.
        await SeedAppAsync(dbFactory, "tool", "executable");

        var (supervisor, allocator) = await CreateSupervisorAsync(dbFactory);

        await supervisor.StartAsync(CancellationToken.None);

        // Nothing in the whole valid range is reserved.
        for (var port = 1; port <= 65535; port += 257)
        {
            allocator.IsReserved(port).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task CleanupDeletedApp_ReleasesThePinnedPort()
    {
        var dbFactory = new TestDbContextFactory(_connection);
        var app = await SeedPinnedExecutableAsync(dbFactory, "searxng", fixedPort: 8888);

        var (supervisor, allocator) = await CreateSupervisorAsync(dbFactory);

        await supervisor.StartAsync(CancellationToken.None);
        allocator.IsReserved(8888).ShouldBeTrue();

        supervisor.CleanupDeletedApp(app.Id, app.Slug);

        allocator.IsReserved(8888).ShouldBeFalse();
    }

    [Fact]
    public async Task StartAsync_PinnedPortStaysReservedAcrossRepeatedBoots()
    {
        var dbFactory = new TestDbContextFactory(_connection);
        await SeedPinnedExecutableAsync(dbFactory, "searxng", fixedPort: 8888);

        // The pin is durable: each boot re-hydrates the reservation from config,
        // so the address a consumer pins stays stable restart after restart.
        for (var boot = 0; boot < 3; boot++)
        {
            var (supervisor, allocator) = await CreateSupervisorAsync(dbFactory);

            await supervisor.StartAsync(CancellationToken.None);

            allocator.IsReserved(8888).ShouldBeTrue();
        }
    }

    // --- Helpers ---

    private static async Task<App> SeedAppAsync
    (
        IDbContextFactory<AppDbContext> dbFactory,
        string slug,
        string appTypeSlug
    )
    {
        await using var context = await dbFactory.CreateDbContextAsync();

        var app = new App
        {
            Slug = slug,
            DisplayName = slug,
            AppTypeSlug = appTypeSlug
        };

        context.Apps.Add(app);
        await context.SaveChangesAsync();

        return app;
    }

    private static async Task<App> SeedPinnedExecutableAsync
    (
        IDbContextFactory<AppDbContext> dbFactory,
        string slug,
        int fixedPort
    )
    {
        var app = await SeedAppAsync(dbFactory, slug, "executable");

        await using var context = await dbFactory.CreateDbContextAsync();

        context.Set<CapabilityOverride>().Add
        (
            new CapabilityOverride
            {
                AppId = app.Id,
                CapabilitySlug = "port-injection",
                ConfigurationJson = JsonSerializer.Serialize(new { fixedPort })
            }
        );

        await context.SaveChangesAsync();

        return app;
    }

    private static async Task<(ProcessSupervisor supervisor, PortAllocator allocator)> CreateSupervisorAsync
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
            CertLifetime = "168h",
            AdminPort = 2019
        };

        var typeStore = new TypeStore
        (
            new EventBus<TypeStoreReloadedEvent>(),
            new TypeStoreSettings { UserTypesDirectory = Path.Combine(Path.GetTempPath(), "collabhost-373-notexist") },
            settings,
            new StubHostEnvironment(),
            NullLogger<TypeStore>.Instance
        );

        await typeStore.LoadAsync();

        var capabilityStore = new CapabilityStore(typeStore, appStore, NullLogger<CapabilityStore>.Instance);
        var eventBus = new EventBus<ProcessStateChangedEvent>();
        var activityEventStore = new ActivityEventStore(dbFactory, NullLogger<ActivityEventStore>.Instance);
        var bundleDirectory = new HostedAppBundleDirectory(Path.GetTempPath(), NullLogger<HostedAppBundleDirectory>.Instance);
        var allocator = new PortAllocator();

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
            allocator,
            activityEventStore,
            NullLogger<ProcessSupervisor>.Instance
        );

        return (supervisor, allocator);
    }
}

// Runner that is never expected to start a process -- the seeded executable apps
// are not set to auto-start, so the hydration pass (not the auto-start loop) is
// what exercises the reservation. A start attempt would surface as a thrown
// NotSupportedException, failing the test loudly rather than silently.
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
