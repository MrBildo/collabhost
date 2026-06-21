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
using Collabhost.Api.Tests.Fixtures;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Supervisor;

// SUP-08: ParkErrorProcess does `_processes[appId] = errorProcess` (an overwrite) without
// disposing the ManagedProcess it replaces. A ManagedProcess owns an IProcessHandle (the OS
// process/job handle) + a containment handle + the restart-delay CTS, all released only in
// ManagedProcess.Dispose(); a crashed-but-not-cleaned process keeps its handle (MarkCrashed nulls
// Pid/Port but never touches _handle).
//
// HONEST SCOPE (disproof): the live leak through StartAppAsync was closed by #424 (the entry
// dispose at StartAppAsync that TryRemove+Disposes any non-Running/non-Starting prior process
// before StartAppInternalAsync runs), so today every caller of StartAppInternalAsync pre-clears the
// slot and ParkErrorProcess overwrites an empty slot in practice. What this test pins is the LOCAL
// invariant the fix establishes -- ParkErrorProcess must be self-defensive like the successful
// start path (`_processes.TryRemove(...); old?.Dispose()`), not rely on every caller having
// pre-disposed. Otherwise the overwrite site is one forgotten pre-dispose away from reintroducing
// the handle leak. The invariant is otherwise unreachable from the public surface (by design, post
// #424), so it is pinned by invoking ParkErrorProcess directly.
#pragma warning disable CA1001 // SqliteConnection owned + disposed via IAsyncLifetime, mirrors sibling supervisor tests.
public class ProcessSupervisorParkErrorProcessTests : IAsyncLifetime
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

    // RED-first: a process holding a tracking handle is sitting in _processes (the residue a real
    // runtime crash leaves -- Crashed, Pid/Port cleared, handle still owned and un-disposed). When
    // ParkErrorProcess parks over it, pre-fix the bare `_processes[appId] = errorProcess` overwrite
    // drops it without disposing -- the handle (and the OS resources it owns) leaks (IsDisposed
    // stays false). Post-fix ParkErrorProcess disposes the replaced process, mirroring the
    // successful start path.
    [Fact]
    public async Task ParkErrorProcess_OverwritesAHandleHoldingProcess_DisposesTheReplaced()
    {
        var dbFactory = new TestDbContextFactory(_connection);
        var app = await SeedAppAsync(dbFactory, "park-leak");

        var (supervisor, _) = BuildSupervisor(dbFactory);

        // A crashed process holding a tracking handle, sitting in _processes.
        var handle = new DisposeTrackingHandle();
        var crashed = new ManagedProcess(app.Id, app.Slug, app.DisplayName);
        crashed.MarkRunning(handle);
        crashed.MarkCrashed(1);
        crashed.State.ShouldBe(ProcessState.Crashed);
        handle.IsDisposed.ShouldBeFalse("the crashed process still owns its handle before the park");

        InjectProcess(supervisor, crashed);

        // Park an error process over it -- the overwrite path under test.
        InvokeParkErrorProcess(supervisor, app, "Cannot start app: artifact location is not configured.");

        // The slot now holds the freshly-parked error process, and the replaced crashed process
        // MUST have been disposed -- otherwise its handle leaks. THIS is SUP-08.
        handle.IsDisposed.ShouldBeTrue("ParkErrorProcess must dispose the ManagedProcess it overwrites");
    }

    private static void InjectProcess(ProcessSupervisor supervisor, ManagedProcess process)
    {
        var dictionary = GetProcessesDictionary(supervisor);

        var indexer = dictionary.GetType().GetProperty("Item")
            ?? throw new InvalidOperationException("ConcurrentDictionary indexer not found.");

        indexer.SetValue(dictionary, process, [process.AppId]);
    }

    private static object GetProcessesDictionary(ProcessSupervisor supervisor)
    {
        var processesField = typeof(ProcessSupervisor)
            .GetField("_processes", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Could not find _processes field on ProcessSupervisor.");

        return processesField.GetValue(supervisor)
            ?? throw new InvalidOperationException("_processes was null on ProcessSupervisor instance.");
    }

    // ParkErrorProcess is private (the asymmetric overwrite site is an internal detail of the start
    // path). The defensive dispose-before-overwrite invariant it must hold is unreachable from the
    // public surface post-#424, so it is pinned by direct invocation -- the only way to assert the
    // overwrite site is locally safe rather than caller-dependent.
    private static void InvokeParkErrorProcess(ProcessSupervisor supervisor, App app, string errorMessage)
    {
        var method = typeof(ProcessSupervisor)
            .GetMethod("ParkErrorProcess", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Could not find ParkErrorProcess on ProcessSupervisor.");

        method.Invoke(supervisor, [app.Id, app, errorMessage, ProcessState.Stopped]);
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

    private static (ProcessSupervisor supervisor, PortAllocator allocator) BuildSupervisor
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
            new TypeStoreSettings { UserTypesDirectory = Path.Combine(Path.GetTempPath(), "collabhost-sup08-notexist") },
            settings,
            new StubHostEnvironment(),
            NullLogger<TypeStore>.Instance
        );

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
            [],
            bundleDirectory,
            allocator,
            activityEventStore,
            NullLogger<ProcessSupervisor>.Instance
        );

        return (supervisor, allocator);
    }
}

// ParkErrorProcess is invoked directly; the runner is never reached -- a Start call would surface
// as a thrown NotSupportedException, failing the test loudly.
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

// A handle that reports "exited" (matching a crashed process's handle) and records disposal. Pid
// is fixed -- the test never reuses it across processes, so a stable value is fine.
file sealed class DisposeTrackingHandle : IProcessHandle
{
    public int Pid => 4848;

    public bool HasExited => true;

    public int? ExitCode => 1;

    public bool IsDisposed { get; private set; }

#pragma warning disable CS0067 // Event is part of the contract; not raised in this fake.
    public event Action<int>? Exited;
#pragma warning restore CS0067

    public bool TryGracefulShutdown() => false;

    public void Kill() { }

    public void Dispose() => IsDisposed = true;
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
