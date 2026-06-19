using System.Net;
using System.Net.Sockets;
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

    public async ValueTask InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var dbFactory = new TestDbContextFactory(_connection);

        await using var context = await dbFactory.CreateDbContextAsync();
        await context.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        await _connection.DisposeAsync();
    }

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

    [Fact]
    public async Task StartAppAsync_PinnedPortHeldByAnExternalOwner_HardFailsAttributablyWithoutStarting()
    {
        // The contract's load-bearing case: a port held by something OUTSIDE
        // Collabhost (here, a real loopback listener standing in for another host
        // service / container / leftover) is invisible to the reservation list.
        // The pinned app must validate live availability and hard-fail with an
        // attributable reason -- not hand a doomed port to the child and let it
        // crash-loop. UnusedRunner throws if Start is reached, so this also proves
        // the failure happens BEFORE any process is launched.
        using var occupier = new TcpListener(IPAddress.Loopback, 0);
        occupier.Start();
        var heldPort = ((IPEndPoint)occupier.LocalEndpoint).Port;

        var dbFactory = new TestDbContextFactory(_connection);
        var app = await SeedStartablePinnedExecutableAsync(dbFactory, "searxng", fixedPort: heldPort);

        var (supervisor, _) = await CreateSupervisorAsync(dbFactory);

        var exception = await Should.ThrowAsync<InvalidOperationException>
        (
            async () => await supervisor.StartAppAsync(app.Id, CancellationToken.None)
        );

        // Operator-facing, attributable: names the unavailable port in plain English.
        exception.Message.ShouldContain(heldPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
        exception.Message.ShouldContain("already in use");

        // Clean, attributable terminal stop -- Fatal, not a crash-loop. Restarting
        // can't mask it: nothing was started, so there is no exit event to drive a
        // retry, and the state is Fatal until the operator clears the conflict.
        var parked = supervisor.GetProcess(app.Id);
        parked.ShouldNotBeNull();
        parked.State.ShouldBe(ProcessState.Fatal);

        occupier.Stop();
    }

    [Fact]
    public async Task StartAppAsync_UnpinTransition_ReleasesThePriorReservationInSession()
    {
        // Unpin symmetry (FixedPort N->0): an app previously pinned to a port must
        // release that reservation when it next starts unpinned -- in the same
        // session, not only on delete or reboot. Seed unpinned (fixedPort 0) but
        // pre-reserve a port as if the app had just been unpinned from it; the
        // start path's else-branch must release it.
        var dbFactory = new TestDbContextFactory(_connection);
        var app = await SeedStartableExecutableAsync(dbFactory, "tool");

        var (supervisor, allocator) = await CreateStartableSupervisorAsync(dbFactory);

        // Simulate the stale reservation a prior pin left behind.
        allocator.Reserve(app.Id, 7777);
        allocator.IsReserved(7777).ShouldBeTrue();

        await supervisor.StartAppAsync(app.Id, CancellationToken.None);

        // The unpinned start released the stale reservation back to the pool.
        allocator.IsReserved(7777).ShouldBeFalse();
    }

    [Fact]
    public void ProxyAdminPortInitializer_AllocatesReservationAwareAndReservesTheAdminPort()
    {
        // Item 2: the proxy admin port is drawn through the shared allocator (so it
        // excludes pins) AND reserved back (so no later app allocation is handed it).
        var settings = new ProxySettings
        {
            BaseDomain = "collab.internal",
            BinaryPath = null,
            ListenAddress = ":443",
            CertLifetime = "168h"
        };

        var allocator = new PortAllocator();

        // A pin already in the registry must be excluded from the admin allocation.
        // Reserve a wide band to make the exclusion gate load-bearing, not chance.
        for (var port = 40000; port <= 40100; port++)
        {
            allocator.Reserve(Ulid.NewUlid(), port);
        }

        var initializer = new ProxyAdminPortInitializer
        (
            settings,
            NullLogger<ProxyAdminPortInitializer>.Instance
        );

        initializer.Initialize(allocator);

        settings.AdminPort.ShouldBeGreaterThan(0);
        allocator.IsReserved(settings.AdminPort).ShouldBeTrue();

        // It did not land on a pinned port.
        (settings.AdminPort is >= 40000 and <= 40100).ShouldBeFalse();
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

    // Seeds an executable that can actually reach StartAppInternalAsync's port
    // branch: a real (existing) artifact directory + a Manual-discovery command +
    // a pinned port. The artifact dir must exist on disk (StartAppInternalAsync
    // checks Directory.Exists before discovery).
    private static async Task<App> SeedStartablePinnedExecutableAsync
    (
        IDbContextFactory<AppDbContext> dbFactory,
        string slug,
        int fixedPort
    )
    {
        var app = await SeedStartableExecutableAsync(dbFactory, slug);

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

    private static async Task<App> SeedStartableExecutableAsync
    (
        IDbContextFactory<AppDbContext> dbFactory,
        string slug
    )
    {
        var app = await SeedAppAsync(dbFactory, slug, "executable");

        var artifactDirectory = Path.Combine
        (
            Path.GetTempPath(),
            "collabhost-373-test-" + Guid.NewGuid().ToString("N")
        );

        Directory.CreateDirectory(artifactDirectory);

        await using var context = await dbFactory.CreateDbContextAsync();

        context.Set<CapabilityOverride>().Add
        (
            new CapabilityOverride
            {
                AppId = app.Id,
                CapabilitySlug = "artifact",
                ConfigurationJson = JsonSerializer.Serialize(new { location = artifactDirectory })
            }
        );

        // Manual discovery needs a command; the value is never executed (the runner
        // is a fake / the start fails before launch on the collision path).
        context.Set<CapabilityOverride>().Add
        (
            new CapabilityOverride
            {
                AppId = app.Id,
                CapabilitySlug = "process",
                ConfigurationJson = JsonSerializer.Serialize(new { command = "noop" })
            }
        );

        await context.SaveChangesAsync();

        return app;
    }

    private static Task<(ProcessSupervisor supervisor, PortAllocator allocator)> CreateSupervisorAsync
    (
        IDbContextFactory<AppDbContext> dbFactory
    ) =>
        BuildSupervisorAsync(dbFactory, new UnusedRunner());

    // Variant whose runner returns a live (already-exited) handle, so an unpinned
    // start actually proceeds past the port branch into the runner -- exercising the
    // else-branch Release. The collision path never reaches the runner, so it can
    // keep UnusedRunner; only the unpin-release path needs a runner that "starts."
    private static Task<(ProcessSupervisor supervisor, PortAllocator allocator)> CreateStartableSupervisorAsync
    (
        IDbContextFactory<AppDbContext> dbFactory
    ) =>
        BuildSupervisorAsync(dbFactory, new StartedHandleRunner());

    private static async Task<(ProcessSupervisor supervisor, PortAllocator allocator)> BuildSupervisorAsync
    (
        IDbContextFactory<AppDbContext> dbFactory,
        IManagedProcessRunner runner
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
            runner,
            new NullContainment(),
            appStore,
            capabilityStore,
            typeStore,
            eventBus,
            [],
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

// Runner that returns a handle so a start can proceed past the port branch. The
// handle reports already-exited so the grace-period task settles quickly without
// a real process; the unpin-release assertion reads the allocator immediately
// after StartAppAsync returns (Release happens synchronously in the else-branch),
// so the post-start lifecycle does not affect it.
file sealed class StartedHandleRunner : IManagedProcessRunner
{
    public IProcessHandle Start(ProcessStartConfiguration configuration) => new StartedHandle();

    public Task<ProcessRunResult> RunToCompletionAsync
    (
        ProcessStartConfiguration configuration,
        TimeSpan timeout,
        CancellationToken ct = default
    ) =>
        Task.FromResult(new ProcessRunResult(0, false));
}

file sealed class StartedHandle : IProcessHandle
{
    public int Pid => 4242;

    public bool HasExited => true;

    public int? ExitCode => 0;

#pragma warning disable CS0067 // Event is part of the contract; not raised in this fake.
    public event Action<int>? Exited;
#pragma warning restore CS0067

    public bool TryGracefulShutdown() => true;

    public void Kill() { }

    public void Dispose() { }
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
