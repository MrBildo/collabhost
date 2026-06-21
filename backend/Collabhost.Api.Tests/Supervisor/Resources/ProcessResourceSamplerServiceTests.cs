using System.Collections.Concurrent;
using System.Reflection;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Data;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Events;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;
using Collabhost.Api.Supervisor;
using Collabhost.Api.Supervisor.Containment;
using Collabhost.Api.Supervisor.Resources;
using Collabhost.Api.Tests.Fixtures;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Supervisor.Resources;

// SUP-15a: the sampler holds per-PID CPU baselines in _previousSamples to compute the CPU% delta.
// When a process stops, SampleAll removes its resource-cache entry but (pre-fix) does NOT Forget
// the PID baseline -- so if the OS reuses that PID for a different process, the stale baseline
// poisons the new process's first CPU% reading. The null-snapshot branch already calls Forget; the
// !IsRunning branch did not. This pins the symmetry: a no-longer-running process's PID baseline is
// forgotten.
#pragma warning disable CA1001 // SqliteConnection owned + disposed via IAsyncLifetime, mirrors sibling supervisor tests.
public class ProcessResourceSamplerServiceTests : IAsyncLifetime
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

    // RED-first: a process that was Running (so its PID baseline is in _previousSamples) is now
    // Stopping -- !IsRunning but still carrying its PID. Pre-fix SampleAll removes the cache entry
    // but skips Forget(pid), leaking the baseline. Post-fix the PID is forgotten so a later
    // PID-reuse cannot inherit a poisoned CPU% baseline.
    [Fact]
    public async Task SampleAll_ProcessNoLongerRunningButRetainsPid_ForgetsThePidBaseline()
    {
        var dbFactory = new TestDbContextFactory(_connection);
        var app = await SeedAppAsync(dbFactory, "sampler-forget");

        var supervisor = BuildSupervisor(dbFactory);
        var sampler = new RecordingSampler();
        var cache = new ProcessResourceCache();

        // A process that ran (PID assigned), then entered Stopping: !IsRunning, but Pid is retained
        // (Stopping is mid-shutdown with a live PID). This is the residue whose baseline must be
        // forgotten.
        const int pid = 5151;
        var process = new ManagedProcess(app.Id, app.Slug, app.DisplayName);
        process.MarkRunning(new FixedPidHandle(pid));
        process.MarkStopping();
        process.IsRunning.ShouldBeFalse();
        process.Pid.ShouldBe(pid);

        InjectProcess(supervisor, process);

        // Prime the cache so the !IsRunning branch has an entry to remove (mirrors a prior sample).
        cache.Set(app.Id, new ProcessResourceSnapshot(10.0, 64.0, 7, DateTime.UtcNow));

        InvokeSampleAll(supervisor, sampler, cache);

        // The cache entry is cleared (existing behavior) AND the PID baseline is forgotten (the fix).
        cache.GetLatest(app.Id).ShouldBeNull();
        sampler.ForgottenPids.ShouldContain(pid);
    }

    private static void InvokeSampleAll
    (
        ProcessSupervisor supervisor,
        IProcessResourceSampler sampler,
        ProcessResourceCache cache
    )
    {
        var service = new ProcessResourceSamplerService
        (
            sampler,
            supervisor,
            cache,
            TimeProvider.System,
            NullLogger<ProcessResourceSamplerService>.Instance
        );

        var method = typeof(ProcessResourceSamplerService)
            .GetMethod("SampleAll", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Could not find SampleAll on ProcessResourceSamplerService.");

        method.Invoke(service, []);
    }

    private static void InjectProcess(ProcessSupervisor supervisor, ManagedProcess process)
    {
        var processesField = typeof(ProcessSupervisor)
            .GetField("_processes", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Could not find _processes field on ProcessSupervisor.");

        var dictionary = processesField.GetValue(supervisor)
            ?? throw new InvalidOperationException("_processes was null on ProcessSupervisor instance.");

        var indexer = dictionary.GetType().GetProperty("Item")
            ?? throw new InvalidOperationException("ConcurrentDictionary indexer not found.");

        indexer.SetValue(dictionary, process, [process.AppId]);
    }

    // --- Helpers ---

    private static async Task<App> SeedAppAsync(IDbContextFactory<AppDbContext> dbFactory, string slug)
    {
        await using var context = await dbFactory.CreateDbContextAsync();

        var app = new App
        {
            Slug = slug,
            DisplayName = slug,
            AppTypeSlug = "executable"
        };

        context.Apps.Add(app);
        await context.SaveChangesAsync();

        return app;
    }

    private static ProcessSupervisor BuildSupervisor(IDbContextFactory<AppDbContext> dbFactory)
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
            new TypeStoreSettings { UserTypesDirectory = Path.Combine(Path.GetTempPath(), "collabhost-sup15-notexist") },
            settings,
            new StubHostEnvironment(),
            NullLogger<TypeStore>.Instance
        );

        var capabilityStore = new CapabilityStore(typeStore, appStore, NullLogger<CapabilityStore>.Instance);
        var eventBus = new EventBus<ProcessStateChangedEvent>();
        var activityEventStore = new ActivityEventStore(dbFactory, NullLogger<ActivityEventStore>.Instance);
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
            activityEventStore,
            NullLogger<ProcessSupervisor>.Instance
        );

        return supervisor;
    }
}

// Records which PIDs were Forget-ed; Sample is never reached for a !IsRunning process (the branch
// under test removes/forgets without sampling).
file sealed class RecordingSampler : IProcessResourceSampler
{
    public ConcurrentBag<int> ForgottenPids { get; } = [];

    public ProcessResourceSnapshot? Sample(int pid) =>
        new(0, 0, 0, DateTime.UtcNow);

    public void Forget(int pid) => ForgottenPids.Add(pid);
}

file sealed class FixedPidHandle(int pid) : IProcessHandle
{
    public int Pid => pid;

    public bool HasExited => false;

    public int? ExitCode => null;

#pragma warning disable CS0067 // Event is part of the contract; not raised in this fake.
    public event Action<int>? Exited;
#pragma warning restore CS0067

    public bool TryGracefulShutdown() => false;

    public void Kill() { }

    public void Dispose() { }
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
