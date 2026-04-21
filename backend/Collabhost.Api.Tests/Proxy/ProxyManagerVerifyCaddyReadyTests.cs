using Collabhost.Api.ActivityLog;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Data;
using Collabhost.Api.Data.AppTypes;
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

// Tests the post-launch admin-API probe per §6.4.2:
// - 5s overall deadline
// - 1s per-attempt timeout
// - 200ms delay between retries
// - returns true on first healthy response within deadline
// - returns false on deadline exhaustion
public class ProxyManagerVerifyCaddyReadyTests
{
    [Fact]
    public async Task VerifyCaddyReadyAsync_HealthyImmediately_ReturnsTrue()
    {
        var caddy = new ScriptedCaddyClient(alwaysReady: true);
        var manager = CreateProxyManager(caddy);

        var result = await manager.VerifyCaddyReadyAsync(CancellationToken.None);

        result.ShouldBeTrue();
        caddy.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task VerifyCaddyReadyAsync_SlowStart_ReturnsTrueWithinDeadline()
    {
        // Simulate slow-start: caddy returns unhealthy for the first two polls, healthy on the
        // third; deadline is 5s so success should land in well under a second.
        var caddy = new ScriptedCaddyClient(readyAfterCalls: 3);
        var manager = CreateProxyManager(caddy);

        var start = DateTime.UtcNow;

        var result = await manager.VerifyCaddyReadyAsync(CancellationToken.None);

        var elapsed = DateTime.UtcNow - start;

        result.ShouldBeTrue();
        caddy.CallCount.ShouldBe(3);
        elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task VerifyCaddyReadyAsync_NeverReady_ReturnsFalseAfter5s()
    {
        var caddy = new ScriptedCaddyClient(alwaysReady: false);
        var manager = CreateProxyManager(caddy);

        var start = DateTime.UtcNow;

        var result = await manager.VerifyCaddyReadyAsync(CancellationToken.None);

        var elapsed = DateTime.UtcNow - start;

        result.ShouldBeFalse();
        // Deadline is 5s; allow slack for loop overhead.
        elapsed.ShouldBeInRange(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(7));
    }

    [Fact]
    public async Task VerifyCaddyReadyAsync_CallerCancelled_ReturnsFalseFast()
    {
        var caddy = new ScriptedCaddyClient(alwaysReady: false);
        var manager = CreateProxyManager(caddy);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var result = await manager.VerifyCaddyReadyAsync(cts.Token);

        result.ShouldBeFalse();
    }

    [Fact]
    public void CurrentState_InitialValue_IsStarting()
    {
        var manager = CreateProxyManager(new ScriptedCaddyClient(alwaysReady: true));

        manager.CurrentState.ShouldBe(ProxyState.Starting);
    }

    private static ProxyManager CreateProxyManager(ICaddyClient caddy)
    {
        var dbFactory = new UnusedDbContextFactory();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var appStore = new AppStore(dbFactory, cache, NullLogger<AppStore>.Instance);

        var settings = new ProxySettings
        {
            BaseDomain = "collab.internal",
            BinaryPath = null,
            ListenAddress = ":443",
            CertLifetime = "168h",
            SelfPort = 58400,
            AdminPort = 2019
        };

        var typeStore = new TypeStore
        (
            new Collabhost.Api.Events.EventBus<TypeStoreReloadedEvent>(),
            new TypeStoreSettings { UserTypesDirectory = Path.Combine(Path.GetTempPath(), "collabhost-test-notexist") },
            settings,
            NullLogger<TypeStore>.Instance
        );

        var capabilityStore = new CapabilityStore(typeStore, appStore, NullLogger<CapabilityStore>.Instance);
        var runner = new UnusedProcessRunner();
        var eventBus = new Collabhost.Api.Events.EventBus<Collabhost.Api.Events.ProcessStateChangedEvent>();
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
            activityEventStore,
            NullLogger<ProxyManager>.Instance
        );
    }
}

// Returns controllable readiness results. Tracks call count for assertions.
file sealed class ScriptedCaddyClient : ICaddyClient
{
    private readonly bool _alwaysReady;
    private readonly int _readyAfterCalls;
    private int _callCount;

    public ScriptedCaddyClient(bool alwaysReady)
    {
        _alwaysReady = alwaysReady;
        _readyAfterCalls = -1;
    }

    public ScriptedCaddyClient(int readyAfterCalls)
    {
        _alwaysReady = false;
        _readyAfterCalls = readyAfterCalls;
    }

    public int CallCount => _callCount;

    public Task<bool> IsReadyAsync(CancellationToken ct = default)
    {
        var current = Interlocked.Increment(ref _callCount);

        return _alwaysReady || (_readyAfterCalls > 0 && current >= _readyAfterCalls)
            ? Task.FromResult(true)
            : Task.FromResult(false);
    }

    public Task<bool> LoadConfigAsync(System.Text.Json.Nodes.JsonObject config, CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<System.Text.Json.Nodes.JsonObject?> GetConfigAsync(CancellationToken ct = default) =>
        Task.FromResult<System.Text.Json.Nodes.JsonObject?>(null);
}

file sealed class UnusedDbContextFactory : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() =>
        throw new NotSupportedException("Not used in probe tests");
#pragma warning disable VSTHRD200 // Async method naming -- interface implementation
    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used in probe tests");
#pragma warning restore VSTHRD200
}

file sealed class UnusedProcessRunner : IManagedProcessRunner
{
    public IProcessHandle Start(ProcessStartConfiguration configuration) =>
        throw new NotSupportedException("Not used in probe tests");

    public Task<ProcessRunResult> RunToCompletionAsync
    (
        ProcessStartConfiguration configuration,
        TimeSpan timeout,
        CancellationToken ct = default
    ) =>
        throw new NotSupportedException("Not used in probe tests");
}
