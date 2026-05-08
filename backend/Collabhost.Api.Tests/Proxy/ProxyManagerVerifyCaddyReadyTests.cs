using System.Reflection;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Data;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Platform;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;
using Collabhost.Api.Supervisor;
using Collabhost.Api.Supervisor.Containment;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Proxy;

// Tests the post-launch admin-API probe:
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
        // third. With virtual time, two 200ms inter-attempt delays elapse logically before
        // the third call returns true -- well under the 5s deadline. Asserting in logical
        // time eliminates the wall-clock flake class that surfaced on PR #154 (card #258).
        var caddy = new ScriptedCaddyClient(readyAfterCalls: 3);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var manager = CreateProxyManager(caddy, clock);

        var start = clock.GetUtcNow();

        var probeTask = manager.VerifyCaddyReadyAsync(CancellationToken.None);

        await DriveProbeLoopAsync(probeTask, clock);

        var elapsed = clock.GetUtcNow() - start;

        (await probeTask).ShouldBeTrue();
        caddy.CallCount.ShouldBe(3);
        // Two 200ms inter-attempt delays before the third (successful) probe call.
        elapsed.ShouldBe(TimeSpan.FromMilliseconds(400));
    }

    [Fact]
    public async Task VerifyCaddyReadyAsync_NeverReady_ReturnsFalseAfter5s()
    {
        // Caddy is never ready. The probe loop must exhaust its 5s deadline and return false.
        // Virtual-time assertion: the deadline check fires when GetUtcNow() >= start + 5s,
        // which lands at exactly 5s of advanced clock (no wall-clock variance).
        var caddy = new ScriptedCaddyClient(alwaysReady: false);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var manager = CreateProxyManager(caddy, clock);

        var start = clock.GetUtcNow();

        var probeTask = manager.VerifyCaddyReadyAsync(CancellationToken.None);

        await DriveProbeLoopAsync(probeTask, clock);

        var elapsed = clock.GetUtcNow() - start;

        (await probeTask).ShouldBeFalse();
        // Loop predicate keeps iterating while UtcNow remains strictly less than the deadline.
        // With 200ms virtual-time steps starting at zero, the loop exits the first iteration
        // where the post-step clock reaches 5s exactly.
        elapsed.ShouldBe(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task VerifyCaddyReadyAsync_CallerCancelled_ReturnsFalseFast()
    {
        // Caller cancellation tripping mid-probe must cause the probe to return false. Use the
        // time-provider-aware CTS constructor so the 300ms cancel timer also runs on virtual
        // time -- consistent with the rest of the file.
        var caddy = new ScriptedCaddyClient(alwaysReady: false);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var manager = CreateProxyManager(caddy, clock);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300), clock);

        var probeTask = manager.VerifyCaddyReadyAsync(cts.Token);

        await DriveProbeLoopAsync(probeTask, clock);

        (await probeTask).ShouldBeFalse();
    }

    [Fact]
    public async Task VerifyCaddyReadyAsync_PerAttemptTimeoutThrows_LoopContinuesToSuccess()
    {
        // Regression for #153 Phase 2 cold-boot bug: when IsReadyAsync throws
        // OperationCanceledException because the per-attempt linked CTS tripped (for
        // example, when a resilience pipeline eats the 1s budget during connection
        // acquisition), the probe loop must treat the exception as a per-attempt timeout
        // and continue, not let it propagate up to ProbeAndActivateAsync's outer OCE
        // catch and silently terminate the probe.
        //
        // Scenario: first attempt throws OCE (simulating the per-attempt CTS firing
        // mid-call); subsequent attempts return healthy. The probe must reach success.
        var caddy = new OceThenReadyCaddyClient(oceCountBeforeReady: 1);
        var manager = CreateProxyManager(caddy);

        var result = await manager.VerifyCaddyReadyAsync(CancellationToken.None);

        result.ShouldBeTrue();
        caddy.CallCount.ShouldBe(2);
    }

    [Fact]
    public async Task VerifyCaddyReadyAsync_EveryAttemptThrowsPerAttemptOce_ReturnsFalseAfter5s()
    {
        // Companion to the loop-continues test: if every attempt throws the per-attempt
        // OCE, the probe must still exhaust its 5s budget and return false (so
        // ProbeAndActivateAsync transitions to Failed + _proxyDisabled). Previously this
        // would bubble OCE into ProbeAndActivateAsync's outer catch and leave the state
        // at Starting forever. Virtual-time variant of the budget-exhaustion assertion.
        var caddy = new OceThenReadyCaddyClient(oceCountBeforeReady: int.MaxValue);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var manager = CreateProxyManager(caddy, clock);

        var start = clock.GetUtcNow();

        var probeTask = manager.VerifyCaddyReadyAsync(CancellationToken.None);

        await DriveProbeLoopAsync(probeTask, clock);

        var elapsed = clock.GetUtcNow() - start;

        (await probeTask).ShouldBeFalse();
        elapsed.ShouldBe(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ProbeAndActivate_PerAttemptOceOnFirstAttempt_StillReachesRunning()
    {
        // End-to-end regression for the cold-boot bug. Before the fix, the per-attempt
        // OCE on the first IsReadyAsync call would propagate out of VerifyCaddyReadyAsync
        // and be swallowed by ProbeAndActivateAsync's "Expected during shutdown" catch.
        // The probe task would vanish and _currentState would stay at Starting for the
        // process lifetime. After the fix, the loop continues and the state reaches
        // Running on the next successful attempt.
        var caddy = new OceThenReadyCaddyClient(oceCountBeforeReady: 1);
        var manager = CreateProxyManager(caddy);

        manager.CurrentState.ShouldBe(ProxyState.Starting);

        await manager.ProbeAndActivateAsync(CancellationToken.None);

        manager.CurrentState.ShouldBe(ProxyState.Running);
    }

    [Fact]
    public void CurrentState_InitialValue_IsStarting()
    {
        var manager = CreateProxyManager(new ScriptedCaddyClient(alwaysReady: true));

        manager.CurrentState.ShouldBe(ProxyState.Starting);
    }

    [Fact]
    public async Task ProbeAndActivate_NeverReady_TransitionsToFailedAndDisables()
    {
        // MED-5: assert the downstream side-effects of the soft-fail path, not just the
        // VerifyCaddyReadyAsync return value. After the 5s probe budget exhausts, the
        // probe-and-activate flow must leave _currentState == Failed and _proxyDisabled == true.
        var caddy = new ScriptedCaddyClient(alwaysReady: false);
        var manager = CreateProxyManager(caddy);

        await manager.ProbeAndActivateAsync(CancellationToken.None);

        manager.CurrentState.ShouldBe(ProxyState.Failed);
        GetProxyDisabled(manager).ShouldBeTrue();
    }

    [Fact]
    public async Task ProbeAndActivate_ReadyFromStarting_TransitionsToRunning()
    {
        var caddy = new ScriptedCaddyClient(alwaysReady: true);
        var manager = CreateProxyManager(caddy);

        manager.CurrentState.ShouldBe(ProxyState.Starting);

        await manager.ProbeAndActivateAsync(CancellationToken.None);

        manager.CurrentState.ShouldBe(ProxyState.Running);
        GetProxyDisabled(manager).ShouldBeFalse();
    }

    [Fact]
    public async Task ProbeAndActivate_CrashedMidProbe_LeavesStateAsFailed()
    {
        // MED-4: directed race test for the MED-1 CAS fix.
        //
        // Scenario: probe is mid-flight when a Crashed event fires. Previous code
        // (unconditional write) would overwrite Failed with Running and mask the crash.
        // After the CAS fix, the probe's late Running write must be suppressed -- state
        // stays at Failed. This test drives that interleave deterministically via a gated
        // CaddyClient: the probe blocks inside IsReadyAsync until we flip the state to
        // Failed, then releases, reads "ready=true", and attempts its CAS.
        using var gate = new ManualResetEventSlim(initialState: false);
        using var caddy = new GatedCaddyClient(gate, readyAfterGate: true);
        var manager = CreateProxyManager(caddy);

        manager.CurrentState.ShouldBe(ProxyState.Starting);

        // Start the probe on a background thread. It will block inside IsReadyAsync
        // waiting on the gate.
        var probeTask = Task.Run(() => manager.ProbeAndActivateAsync(CancellationToken.None));

        // Wait until the probe has entered IsReadyAsync (caddy client set the arrival flag).
        caddy.WaitForArrival(TimeSpan.FromSeconds(2)).ShouldBeTrue();

        // Simulate the Crashed event handler landing while the probe is still blocked.
        // Direct field write mimics what HandleProxyAppStateChange would do.
        SetCurrentState(manager, ProxyState.Failed);

        // Release the probe. It observes ready=true, tries to CAS from Starting->Running,
        // sees the field is already Failed, and must suppress the write.
        gate.Set();

        await probeTask;

        manager.CurrentState.ShouldBe(ProxyState.Failed);
    }

    [Fact]
    public async Task ProbeAndActivate_StoppedMidProbeNeverReady_LeavesStateAsStopped()
    {
        // Companion to the Crashed-mid-probe race: during the 5s probe budget a Stopped
        // event lands, then the probe never sees Caddy ready. The probe's Failed write
        // must also CAS from Starting, so Stopped is preserved.
        var caddy = new ScriptedCaddyClient(alwaysReady: false);
        var manager = CreateProxyManager(caddy);

        // Pretend the event handler wrote Stopped while the probe was mid-loop.
        SetCurrentState(manager, ProxyState.Stopped);

        await manager.ProbeAndActivateAsync(CancellationToken.None);

        manager.CurrentState.ShouldBe(ProxyState.Stopped);
        // _proxyDisabled still latches -- the probe is giving up on this process lifetime
        // even though it defers to the event handler for the public-facing state.
        GetProxyDisabled(manager).ShouldBeTrue();
    }

    private static bool GetProxyDisabled(ProxyManager manager)
    {
        var field = typeof(ProxyManager).GetField
        (
            "_proxyDisabled",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        field.ShouldNotBeNull();

        return (bool)field.GetValue(manager)!;
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

    private static ProxyManager CreateProxyManager(ICaddyClient caddy, TimeProvider? timeProvider = null)
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
            AdminPort = 2019
        };

        var hostingSettings = new HostingSettings { ListenPort = 58400 };

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
            hostingSettings,
            new Collabhost.Api.Portal.PortalSettings { Subdomain = "collabhost" },
            activityEventStore,
            timeProvider ?? TimeProvider.System,
            NullLogger<ProxyManager>.Instance
        );
    }

    // Drives a probe-loop task to completion under a FakeTimeProvider clock by advancing
    // time in 200ms steps (the probe loop's inter-attempt delay) until the task completes.
    //
    // The probe's `await Task.Delay(..., timeProvider, ct)` continuation runs on the thread
    // pool when the FakeTimeProvider fires its timer. The test thread can't directly observe
    // that continuation; a short real-wall-clock yield (Task.Delay(1)) gives the runtime a
    // chance to schedule and run the continuation before the next Advance. The 1ms cost is
    // bounded by the iteration count, not by virtual time, so this stays deterministic.
    //
    // Bounded to 100 iterations (~20s of virtual time, well past the probe's 5s budget) so
    // a buggy probe loop fails fast instead of hanging the test.
    private static async Task DriveProbeLoopAsync(Task probeTask, FakeTimeProvider clock)
    {
        for (var i = 0; i < 100; i++)
        {
            // Real-time yield: lets the probe's thread-pool continuation drain before we
            // Advance again. Required for assertions that depend on the probe having
            // actually observed the post-advance state (cancellation, deadline expiry).
            await Task.Delay(1);

            if (probeTask.IsCompleted)
            {
                return;
            }

            clock.Advance(TimeSpan.FromMilliseconds(200));
        }

        // Final drain after the last advance.
        await Task.Delay(1);

        probeTask.IsCompleted.ShouldBeTrue
        (
            "probe loop did not complete after advancing virtual time well past the 5s budget"
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

    public Task<LoadConfigResult> LoadConfigAsync(System.Text.Json.Nodes.JsonObject config, CancellationToken ct = default) =>
        Task.FromResult(LoadConfigResult.Ok());

    public Task<System.Text.Json.Nodes.JsonObject?> GetConfigAsync(CancellationToken ct = default) =>
        Task.FromResult<System.Text.Json.Nodes.JsonObject?>(null);
}

// Throws OperationCanceledException for the first N calls (simulating the per-attempt
// CTS tripping mid-IsReadyAsync), then returns healthy. Drives the regression tests for
// the #153 Phase 2 cold-boot stuck-starting bug.
file sealed class OceThenReadyCaddyClient
(
    int oceCountBeforeReady
)
    : ICaddyClient
{
    private readonly int _oceCountBeforeReady = oceCountBeforeReady;
    private int _callCount;

    public int CallCount => _callCount;

    public Task<bool> IsReadyAsync(CancellationToken ct = default)
    {
        var current = Interlocked.Increment(ref _callCount);

        if (current <= _oceCountBeforeReady)
        {
            // Mirror what the real CaddyClient does when its linked CTS trips during
            // HttpClient.GetAsync: the underlying TaskCanceledException has
            // ct.IsCancellationRequested == true, so CaddyClient's narrow catch filter
            // rejects it and the exception propagates out.
            return Task.FromException<bool>(new OperationCanceledException(ct));
        }

        return Task.FromResult(true);
    }

    public Task<LoadConfigResult> LoadConfigAsync(System.Text.Json.Nodes.JsonObject config, CancellationToken ct = default) =>
        Task.FromResult(LoadConfigResult.Ok());

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

// Blocks inside IsReadyAsync until the gate is signalled. Used to orchestrate the
// directed MED-1 race (probe mid-flight when an event-handler write lands).
file sealed class GatedCaddyClient(ManualResetEventSlim gate, bool readyAfterGate) : ICaddyClient, IDisposable
{
    private readonly ManualResetEventSlim _gate = gate;
    private readonly bool _readyAfterGate = readyAfterGate;
    private readonly ManualResetEventSlim _arrival = new(initialState: false);

    public bool WaitForArrival(TimeSpan timeout) =>
        _arrival.Wait(timeout);

    public Task<bool> IsReadyAsync(CancellationToken ct = default)
    {
        _arrival.Set();
        _gate.Wait(ct);

        return Task.FromResult(_readyAfterGate);
    }

    public Task<LoadConfigResult> LoadConfigAsync(System.Text.Json.Nodes.JsonObject config, CancellationToken ct = default) =>
        Task.FromResult(LoadConfigResult.Ok());

    public Task<System.Text.Json.Nodes.JsonObject?> GetConfigAsync(CancellationToken ct = default) =>
        Task.FromResult<System.Text.Json.Nodes.JsonObject?>(null);

    public void Dispose() =>
        _arrival.Dispose();
}
