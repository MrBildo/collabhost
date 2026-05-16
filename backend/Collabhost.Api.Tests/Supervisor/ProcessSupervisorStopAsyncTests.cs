using System.Diagnostics;
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

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Supervisor;

// Covers #191: StopAsync must honor the host's cancellation token. Per Marcus's investigation,
// the inner StopProcessWithShutdownPolicyAsync / SendGracefulShutdownAsync chain previously
// dropped the token at the first frame; a misconfigured app's per-process timeout could exceed
// the host budget. The fix links the host token with the per-app CTS so whichever fires first
// wins and falls through to hard kill.
public class ProcessSupervisorStopAsyncTests
{
    [Fact]
    public async Task StopAsync_HostTokenFires_ForceKillsHungProcessWithinHostBudget()
    {
        var supervisor = CreateSupervisor();

        var hungHandle = new HungProcessHandle();
        var process = new ManagedProcess(Ulid.NewUlid(), "test-app", "Test App");

        // Put the process into Running state with a hung handle (graceful signal sent
        // successfully, but the process never exits). With the bug, this would block
        // for the full default 10s ShutdownTimeoutSeconds regardless of host token.
        process.MarkRunning(hungHandle);

        InjectProcess(supervisor, process);

        // Host gives the supervisor a 1-second budget.
        using var hostBudget = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        var stopwatch = Stopwatch.StartNew();
        await supervisor.StopAsync(hostBudget.Token);
        stopwatch.Stop();

        // Polling loop is 250ms granular; allow a small tolerance over the 1s host budget.
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));

        // The hung process must be hard-killed once the host budget elapses.
        hungHandle.KillCount.ShouldBe(1);
    }

    [Fact]
    public async Task StopAsync_CancelsPendingRestartDelay_BeforeIteratingProcesses()
    {
        var supervisor = CreateSupervisor();

        // Process in Backoff state with a pending restart-delay CTS -- mirrors the
        // OnStartupFailure / OnRuntimeCrash sites at ProcessSupervisor.cs ~:622, ~:815.
        // Without the cancel-before-loop, the Task.Delay would survive past
        // ApplicationStopping and StartAppInternalAsync would fire on torn-down state.
        var process = new ManagedProcess(Ulid.NewUlid(), "delayed-app", "Delayed App");
        process.MarkStarting();
        process.MarkBackoff(1);

        var restartCancellation = new CancellationTokenSource();
        process.SetRestartDelayCancellation(restartCancellation);

        InjectProcess(supervisor, process);

        await supervisor.StopAsync(CancellationToken.None);

        // The CTS handed to the restart-delay Task.Run was cancelled by StopAsync's
        // cancel-before-loop sweep -- any in-flight Task.Delay would throw and the
        // OperationCanceledException catch block would log debug + exit silently.
        restartCancellation.IsCancellationRequested.ShouldBeTrue();
    }

    private static ProcessSupervisor CreateSupervisor()
    {
        var dbFactory = new ThrowingDbContextFactory();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var appStore = new AppStore(dbFactory, cache, NullLogger<AppStore>.Instance);

        var typeStore = new TypeStore
        (
            new EventBus<TypeStoreReloadedEvent>(),
            new TypeStoreSettings { UserTypesDirectory = Path.Combine(Path.GetTempPath(), "collabhost-test-notexist-" + Guid.NewGuid().ToString("N")) },
            new ProxySettings { BaseDomain = "collab.internal", BinaryPath = "caddy", ListenAddress = ":443", CertLifetime = "168h", AdminPort = 2019 },
            new StubHostEnvironment(),
            NullLogger<TypeStore>.Instance
        );

        var capabilityStore = new CapabilityStore(typeStore, appStore, NullLogger<CapabilityStore>.Instance);
        var eventBus = new EventBus<ProcessStateChangedEvent>();
        var activityEventStore = new ActivityEventStore(dbFactory, NullLogger<ActivityEventStore>.Instance);

        return new ProcessSupervisor
        (
            new UnusedRunner(),
            new NullContainment(),
            appStore,
            capabilityStore,
            typeStore,
            eventBus,
            argumentProviders: [],
            environmentProviders: [],
            new HostedAppBundleDirectory(Path.GetTempPath(), NullLogger<HostedAppBundleDirectory>.Instance),
            activityEventStore,
            NullLogger<ProcessSupervisor>.Instance
        );
    }

    // Inject directly into the private _processes dictionary -- the alternative
    // (going through StartAppAsync) would require a real DB-backed App and a working
    // capability resolution chain, which is overkill for testing the StopAsync behavior.
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
}

// Sealed: file-scoped test fake. TryGracefulShutdown returns true so SendGracefulShutdownAsync
// enters the polling loop. HasExited stays false so the loop only exits when the linked CTS
// fires (host budget or per-app timeout, whichever first).
file sealed class HungProcessHandle : IProcessHandle
{
    public int Pid => 4242;

    public bool HasExited => false;

    public int? ExitCode => null;

    public int KillCount { get; private set; }

#pragma warning disable CS0067 // Event is part of the IProcessHandle contract; not raised in this fake
    public event Action<int>? Exited;
#pragma warning restore CS0067

    public bool TryGracefulShutdown() => true;

    public void Kill() => KillCount++;

    public void Dispose() { }
}

file sealed class UnusedRunner : IManagedProcessRunner
{
    public IProcessHandle Start(ProcessStartConfiguration configuration) =>
        throw new NotSupportedException("Runner.Start should not be invoked in StopAsync tests.");

    public Task<ProcessRunResult> RunToCompletionAsync
    (
        ProcessStartConfiguration configuration,
        TimeSpan timeout,
        CancellationToken ct = default
    ) =>
        throw new NotSupportedException("Runner.RunToCompletionAsync should not be invoked in StopAsync tests.");
}

// AppStore.GetByIdAsync uses IMemoryCache.GetOrCreateAsync; with no cached entry the lambda
// runs and asks for a DbContext. Returning a context backed by a non-existent SQLite file
// would still try to open the DB; throwing here keeps the test honest -- if StopAsync ever
// stops swallowing the missing-app branch, the test fails loudly rather than silently.
file sealed class ThrowingDbContextFactory : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() =>
        throw new NotSupportedException("DB access should not occur in StopAsync tests.");

#pragma warning disable VSTHRD200 // Async method naming -- interface implementation
    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("DB access should not occur in StopAsync tests.");
#pragma warning restore VSTHRD200
}
