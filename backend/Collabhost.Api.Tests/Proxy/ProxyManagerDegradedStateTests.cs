using System.Reflection;
using System.Text.Json.Nodes;

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

namespace Collabhost.Api.Tests.Proxy;

// Tests the Running <-> Degraded transition paths added by card #217. SyncRoutesAsync
// loads apps from the DB before talking to Caddy; we wire an in-memory SQLite so the
// load step succeeds with zero apps, then exercise the CAS edges by flipping
// LoadConfigAsync's result on the fake Caddy client.
//
// CA1001: this class owns an IDisposable SqliteConnection but uses IAsyncLifetime
// for its async cleanup; the suppression mirrors the same pattern used in
// CapabilityStoreTests / ProxyAppSeederAtomicityTests.
#pragma warning disable CA1001
public class ProxyManagerDegradedStateTests : IAsyncLifetime
#pragma warning restore CA1001
{
    private SqliteConnection _connection = default!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var dbFactory = new InMemoryDbContextFactory(_connection);

        await using var context = await dbFactory.CreateDbContextAsync();
        await context.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => _connection.DisposeAsync().AsTask();

    [Fact]
    public async Task SyncRoutes_LoadFails_FromRunning_TransitionsToDegraded()
    {
        var caddy = new SyncControllableCaddyClient();
        caddy.SetLoadResult(LoadConfigResult.Failed(400, "loading config: listening on :443: bind: permission denied"));

        var manager = CreateProxyManager(caddy);
        SetCurrentState(manager, ProxyState.Running);

        await manager.SyncRoutesAsync(CancellationToken.None);

        manager.CurrentState.ShouldBe(ProxyState.Degraded);

        var outcome = manager.LastSyncOutcome;
        outcome.Attempted.ShouldBeTrue();
        outcome.Success.ShouldBeFalse();

        var error = outcome.ErrorMessage;
        error.ShouldNotBeNull();
        error.ShouldContain("permission denied");
    }

    [Fact]
    public async Task SyncRoutes_LoadFails_FromStarting_DoesNotChangeState()
    {
        // CAS Running -> Degraded is the only legal transition; from Starting the sync
        // must not flip into Degraded (the probe owns the Starting window).
        var caddy = new SyncControllableCaddyClient();
        caddy.SetLoadResult(LoadConfigResult.Failed(400, "boom"));

        var manager = CreateProxyManager(caddy);

        manager.CurrentState.ShouldBe(ProxyState.Starting);

        await manager.SyncRoutesAsync(CancellationToken.None);

        manager.CurrentState.ShouldBe(ProxyState.Starting);
    }

    [Fact]
    public async Task SyncRoutes_SuccessAfterFailure_RecoversFromDegradedToRunning()
    {
        var caddy = new SyncControllableCaddyClient();
        caddy.SetLoadResult(LoadConfigResult.Failed(400, "bind: permission denied"));

        var manager = CreateProxyManager(caddy);
        SetCurrentState(manager, ProxyState.Running);

        await manager.SyncRoutesAsync(CancellationToken.None);

        manager.CurrentState.ShouldBe(ProxyState.Degraded);

        // Operator runs setcap, restarts Caddy, the next sync now succeeds.
        caddy.SetLoadResult(LoadConfigResult.Ok());

        await manager.SyncRoutesAsync(CancellationToken.None);

        manager.CurrentState.ShouldBe(ProxyState.Running);

        var outcome = manager.LastSyncOutcome;
        outcome.Success.ShouldBeTrue();
        outcome.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task SyncRoutes_LoadFails_FromStopped_DoesNotChangeState()
    {
        // Operator stopped the proxy. A late sync attempt must not overwrite Stopped.
        var caddy = new SyncControllableCaddyClient();
        caddy.SetLoadResult(LoadConfigResult.Failed(400, "boom"));

        var manager = CreateProxyManager(caddy);
        SetCurrentState(manager, ProxyState.Stopped);

        await manager.SyncRoutesAsync(CancellationToken.None);

        manager.CurrentState.ShouldBe(ProxyState.Stopped);
    }

    [Fact]
    public async Task SyncRoutes_SuccessFromStopped_DoesNotPromoteToRunning()
    {
        // Recovery edge is Degraded -> Running only. A successful sync from Stopped
        // (e.g., a stale RequestSync arriving after stop) must not resurrect the proxy.
        var caddy = new SyncControllableCaddyClient();
        caddy.SetLoadResult(LoadConfigResult.Ok());

        var manager = CreateProxyManager(caddy);
        SetCurrentState(manager, ProxyState.Stopped);

        await manager.SyncRoutesAsync(CancellationToken.None);

        manager.CurrentState.ShouldBe(ProxyState.Stopped);

        manager.LastSyncOutcome.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task SyncRoutes_NeverAttempted_LastSyncOutcomeIsSentinel()
    {
        var caddy = new SyncControllableCaddyClient();
        var manager = CreateProxyManager(caddy);

        var initial = manager.LastSyncOutcome;
        initial.Attempted.ShouldBeFalse();
        initial.Success.ShouldBeFalse();
        initial.LastSyncAt.ShouldBeNull();
        initial.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task SyncRoutes_FromDegraded_FailureKeepsDegraded()
    {
        // Two sync failures in a row: state stays Degraded; the LastSyncOutcome reflects
        // the most recent error so the operator sees "is this still failing for the same reason?".
        var caddy = new SyncControllableCaddyClient();
        caddy.SetLoadResult(LoadConfigResult.Failed(400, "first error"));

        var manager = CreateProxyManager(caddy);
        SetCurrentState(manager, ProxyState.Running);

        await manager.SyncRoutesAsync(CancellationToken.None);

        manager.CurrentState.ShouldBe(ProxyState.Degraded);

        caddy.SetLoadResult(LoadConfigResult.Failed(400, "second error"));

        await manager.SyncRoutesAsync(CancellationToken.None);

        manager.CurrentState.ShouldBe(ProxyState.Degraded);

        var error = manager.LastSyncOutcome.ErrorMessage;
        error.ShouldNotBeNull();
        error.ShouldContain("second error");
    }

    private static void SetCurrentState(ProxyManager manager, ProxyState state)
    {
        var field = typeof(ProxyManager).GetField
        (
            "_currentState",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        field.ShouldNotBeNull();
        field.SetValue(manager, (int)state);
    }

    private ProxyManager CreateProxyManager(ICaddyClient caddy)
    {
        var dbFactory = new InMemoryDbContextFactory(_connection);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var appStore = new AppStore(dbFactory, cache, NullLogger<AppStore>.Instance);

        var settings = new ProxySettings
        {
            BaseDomain = "collab.internal",
            BinaryPath = null,
            ListenAddress = ":80,:443",
            CertLifetime = "168h",
            AdminPort = 2019
        };

        var hostingSettings = new HostingSettings { ListenAddress = "localhost", ListenPort = 58400 };

        var typeStore = new TypeStore
        (
            new EventBus<TypeStoreReloadedEvent>(),
            new TypeStoreSettings { UserTypesDirectory = Path.Combine(Path.GetTempPath(), "collabhost-degraded-test-notexist") },
            settings,
            new StubHostEnvironment(),
            NullLogger<TypeStore>.Instance
        );

        var capabilityStore = new CapabilityStore(typeStore, appStore, NullLogger<CapabilityStore>.Instance);
        var runner = new UnusedProcessRunner();
        var eventBus = new EventBus<ProcessStateChangedEvent>();
        var activityEventStore = new ActivityEventStore(dbFactory, NullLogger<ActivityEventStore>.Instance);

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
            new HostedAppBundleDirectory(Path.GetTempPath(), NullLogger<HostedAppBundleDirectory>.Instance),
            activityEventStore,
            NullLogger<ProcessSupervisor>.Instance
        );

        return new ProxyManager
        (
            caddy,
            appStore,
            capabilityStore,
            typeStore,
            supervisor,
            eventBus,
            settings,
            hostingSettings,
            new Collabhost.Api.Portal.PortalSettings { Subdomain = "collabhost" },
            activityEventStore,
            TimeProvider.System,
            NullLogger<ProxyManager>.Instance
        );
    }
}

// Caddy fake whose LoadConfigAsync result can be flipped between calls, so a single
// test can exercise both the failure and the recovery edge.
file sealed class SyncControllableCaddyClient : ICaddyClient
{
    private LoadConfigResult _result = LoadConfigResult.Ok();

    public void SetLoadResult(LoadConfigResult result) =>
        _result = result;

    public Task<bool> IsReadyAsync(CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<LoadConfigResult> LoadConfigAsync(JsonObject config, CancellationToken ct = default) =>
        Task.FromResult(_result);

    public Task<JsonObject?> GetConfigAsync(CancellationToken ct = default) =>
        Task.FromResult<JsonObject?>(null);
}

file sealed class UnusedProcessRunner : IManagedProcessRunner
{
    public IProcessHandle Start(ProcessStartConfiguration configuration) =>
        throw new NotSupportedException("Not used in degraded-state tests");

    public Task<ProcessRunResult> RunToCompletionAsync
    (
        ProcessStartConfiguration configuration,
        TimeSpan timeout,
        CancellationToken ct = default
    ) =>
        throw new NotSupportedException("Not used in degraded-state tests");
}

file sealed class InMemoryDbContextFactory
(
    SqliteConnection connection
) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .ConfigureWarnings
            (
                warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning)
            )
            .Options;

        return new AppDbContext(options);
    }

#pragma warning disable VSTHRD200 // Async method naming -- interface implementation
    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
#pragma warning restore VSTHRD200
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .ConfigureWarnings
            (
                warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning)
            )
            .Options;

        return Task.FromResult(new AppDbContext(options));
    }
}
