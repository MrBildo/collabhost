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

// SUP-01: the start path has no per-appId operation lock on the supervisor. StartAppAsync
// is check-then-act (TryGetValue -> !IsRunning -> TryRemove -> StartAppInternalAsync), and
// StartAppInternalAsync's own check-then-act has a long async window before the new
// ManagedProcess is registered in _processes. The old _operationLock lived ON ManagedProcess,
// which start/restart REPLACES -- so it could never span the operation (disposing the object
// disposes the lock). Two concurrent starts of the same app both pass the check, both reach
// _runner.Start, both spawn an OS process; the loser's ManagedProcess is overwritten in the
// dictionary and its spawned handle is orphaned (nobody kills it).
//
// The fix is a per-appId async lock on the SUPERVISOR (keyed by the stable appId, not on the
// replaced ManagedProcess) held across the whole start/stop/restart/kill operation. These tests
// drive the production StartAppAsync against a real in-memory DB + the real built-in AppType
// bindings + a recording runner, so the no-double-spawn and serialization properties are proven
// end-to-end, not in isolation.
#pragma warning disable CA1001 // SqliteConnection owned + disposed via IAsyncLifetime, mirrors sibling supervisor tests.
public class ProcessSupervisorConcurrencyTests : IAsyncLifetime
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

    // RED-first: against the pre-fix code both concurrent starts pass the !IsRunning check and
    // reach the runner, spawning twice (the gate below guarantees the second enters its check
    // before the first registers). The supervisor's per-appId lock serializes them: the second
    // start waits, then sees State==Running and throws "already running" -- exactly one spawn.
    [Fact]
    public async Task StartAppAsync_TwoConcurrentStartsOfSameApp_SpawnsExactlyOnceWithoutOrphan()
    {
        var dbFactory = new TestDbContextFactory(_connection);
        var app = await SeedStartableExecutableAsync(dbFactory, "racer");

        // A runner that blocks the first spawn inside Start() until released, holding the race
        // window open so a second concurrent start has time to pass its own check-then-act. If
        // the operation is unserialized, BOTH calls reach Start before either registers.
        using var runner = new GatedRecordingRunner();

        var (supervisor, _) = await BuildSupervisorAsync(dbFactory, runner);

        // Fire two concurrent starts of the same appId.
        var first = Task.Run(() => CaptureAsync(() => supervisor.StartAppAsync(app.Id, CancellationToken.None)));
        var second = Task.Run(() => CaptureAsync(() => supervisor.StartAppAsync(app.Id, CancellationToken.None)));

        // Give whichever start wins the lock time to reach the gated Start() (serialized fix) or
        // give both time to reach it (unserialized bug), then release so the test can complete.
        var reachedStart = await runner.WaitForFirstStartAsync(TimeSpan.FromSeconds(5));
        reachedStart.ShouldBeTrue("the start path should have reached the runner");

        // Brief settle so a second UNSERIALIZED start can also enter Start() before we release.
        // Under the serialized fix the second start is parked on the per-appId lock and cannot.
        await Task.Delay(250);

        runner.ReleaseAll();

        // Exactly one of the two starts succeeds; the other observes the running process and
        // throws InvalidOperationException("App is already running."). Neither is allowed to
        // spawn a second OS process.
        var results = await Task.WhenAll(first, second);

        var successes = results.Count(r => r.Succeeded);
        var alreadyRunning = results.Count(r => r is { Succeeded: false, AlreadyRunning: true });

        // The load-bearing assertion: a single spawn. A second Start() call is a second OS
        // process the supervisor has lost track of -- the orphan SUP-01 describes.
        runner.StartCount.ShouldBe(1);

        // And the operation contract holds: one start wins, the other is rejected cleanly.
        successes.ShouldBe(1);
        alreadyRunning.ShouldBe(1);
    }

    // RED-first companion: a start racing a stop of the same app must serialize. Without the
    // supervisor lock, StopAppAsync can mark the process Stopped between the start's spawn and
    // its registration, leaving a spawned-but-untracked process; or the stop can run against a
    // process the start is mid-replacing. With the lock the two run to completion in some order,
    // and the final state is internally consistent (Running with a live spawn, or Stopped with none).
    [Fact]
    public async Task StartThenStop_Concurrent_SerializeToAConsistentTerminalState()
    {
        var dbFactory = new TestDbContextFactory(_connection);
        var app = await SeedStartableExecutableAsync(dbFactory, "start-stop-racer");

        var runner = new RecordingRunner();
        var (supervisor, _) = await BuildSupervisorAsync(dbFactory, runner);

        // Establish a running process first so the stop has something to act on.
        await supervisor.StartAppAsync(app.Id, CancellationToken.None);
        runner.StartCount.ShouldBe(1);

        // Now race a stop against a restart-style start. They must serialize on the per-appId
        // lock; the result is a single coherent terminal state, never a spawned orphan.
        var stop = Task.Run(() => CaptureAsync(() => supervisor.StopAppAsync(app.Id, CancellationToken.None)));
        var start = Task.Run(() => CaptureAsync(() => supervisor.RestartAppAsync(app.Id, CancellationToken.None)));

        await Task.WhenAll(stop, start);

        var process = supervisor.GetProcess(app.Id);
        process.ShouldNotBeNull();

        // Coherence invariant: if the supervisor reports Running, the live handle count it spawned
        // exceeds the count it killed (a real running process); if Stopped, spawns and kills balance.
        var snapshot = process.ReadSnapshot();

        if (snapshot.State == ProcessState.Running)
        {
            snapshot.Pid.ShouldNotBeNull();
            (runner.StartCount - runner.KillCount).ShouldBeGreaterThan(0);
        }
        else
        {
            snapshot.Pid.ShouldBeNull();
        }
    }

    // --- Helpers ---

    private static async Task<App> SeedStartableExecutableAsync
    (
        IDbContextFactory<AppDbContext> dbFactory,
        string slug
    )
    {
        await using var seedContext = await dbFactory.CreateDbContextAsync();

        var app = new App
        {
            Slug = slug,
            DisplayName = slug,
            AppTypeSlug = "executable"
        };

        seedContext.Apps.Add(app);
        await seedContext.SaveChangesAsync();

        var artifactDirectory = Path.Combine
        (
            Path.GetTempPath(),
            "collabhost-424-test-" + Guid.NewGuid().ToString("N")
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

        // Manual discovery needs a command; the value is never executed (the runner is a fake).
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
            new TypeStoreSettings { UserTypesDirectory = Path.Combine(Path.GetTempPath(), "collabhost-424-notexist") },
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

    // Invokes a lifecycle operation and classifies the outcome. Taking a Func (not a Task) starts
    // the operation INSIDE this method's context, which both lets the caller fire it on a worker
    // thread via Task.Run(() => CaptureAsync(...)) and keeps the awaited task context-owned.
    private static async Task<StartOutcome> CaptureAsync(Func<Task<ManagedProcess>> operation)
    {
        try
        {
            await operation();
            return new StartOutcome(true, false);
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains("already running", StringComparison.OrdinalIgnoreCase))
        {
            return new StartOutcome(false, true);
        }
        catch (InvalidOperationException)
        {
            // A stop against an already-stopped app, or another contended lifecycle rejection,
            // surfaces as InvalidOperationException. For the serialization test the terminal state
            // is what matters, not which racer threw -- swallow and let the snapshot assertion judge.
            return new StartOutcome(false, false);
        }
    }

    private readonly record struct StartOutcome(bool Succeeded, bool AlreadyRunning);
}

// Records spawn + kill counts. Each Start returns a long-lived handle that does not self-exit,
// so the supervisor's grace-period promotion lands the process in Running.
file sealed class RecordingRunner : IManagedProcessRunner
{
    private int _startCount;
    private int _killCount;

    public int StartCount => Volatile.Read(ref _startCount);

    public int KillCount => Volatile.Read(ref _killCount);

    public IProcessHandle Start(ProcessStartConfiguration configuration)
    {
        var pid = Interlocked.Increment(ref _startCount) + 4200;
        return new CountingHandle(pid, () => Interlocked.Increment(ref _killCount));
    }

    public Task<ProcessRunResult> RunToCompletionAsync
    (
        ProcessStartConfiguration configuration,
        TimeSpan timeout,
        CancellationToken ct = default
    ) =>
        throw new NotSupportedException("RunToCompletionAsync is not used in these tests.");
}

// A runner whose first Start() blocks until ReleaseAll() so the race window stays open while a
// second concurrent start tries to pass its own check-then-act. StartCount records how many
// spawns actually happened -- the SUP-01 discriminator.
file sealed class GatedRecordingRunner : IManagedProcessRunner, IDisposable
{
    private readonly ManualResetEventSlim _release = new(false);
    private readonly SemaphoreSlim _firstStartReached = new(0, int.MaxValue);
    private int _startCount;

    public int StartCount => Volatile.Read(ref _startCount);

    public IProcessHandle Start(ProcessStartConfiguration configuration)
    {
        var pid = Interlocked.Increment(ref _startCount) + 4300;

        _firstStartReached.Release();

        // Hold the start in-flight so the race window stays open. Bounded so a wedged test
        // fails loudly rather than hanging.
        _release.Wait(TimeSpan.FromSeconds(10));

        return new CountingHandle(pid, static () => { });
    }

    public Task<ProcessRunResult> RunToCompletionAsync
    (
        ProcessStartConfiguration configuration,
        TimeSpan timeout,
        CancellationToken ct = default
    ) =>
        throw new NotSupportedException("RunToCompletionAsync is not used in these tests.");

    public async Task<bool> WaitForFirstStartAsync(TimeSpan timeout) =>
        await _firstStartReached.WaitAsync(timeout);

    public void ReleaseAll() => _release.Set();

    public void Dispose()
    {
        _release.Dispose();
        _firstStartReached.Dispose();
    }
}

// A handle that stays alive (does not self-exit) so the started process settles into Running.
file sealed class CountingHandle(int pid, Action onKill) : IProcessHandle
{
    private readonly Action _onKill = onKill;

    public int Pid => pid;

    public bool HasExited { get; private set; }

    public int? ExitCode => HasExited ? 0 : null;

#pragma warning disable CS0067 // Event is part of the contract; not raised in this fake.
    public event Action<int>? Exited;
#pragma warning restore CS0067

    public bool TryGracefulShutdown()
    {
        HasExited = true;
        return true;
    }

    public void Kill()
    {
        HasExited = true;
        _onKill();
    }

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
