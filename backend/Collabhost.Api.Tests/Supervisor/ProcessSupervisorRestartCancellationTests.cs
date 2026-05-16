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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Supervisor;

// Covers #312 (CH-B): the disposed-CancellationTokenSource race on the restart path, at
// both call sites -- OnStartupFailure (retry closure) and OnRuntimeCrash (auto-restart
// closure). Each scheduled a fire-and-forget Task.Run that captured the
// CancellationTokenSource *instance*. The buggy closure body itself disposes the prior
// ManagedProcess for the same appId (the "remove old process before disposing it" step:
// _processes.TryRemove(appId, out var stale); stale?.Dispose();), and that process is the
// one holding this very CTS via SetRestartDelayCancellation. ManagedProcess.Dispose() ->
// CancelPendingRestart() -> Cancel() + Dispose() on the CTS. The closure then re-reads
// cancellation.Token at StartAppInternalAsync(appId, cancellation.Token) -- get_Token() on
// a disposed source throws ObjectDisposedException, which escapes past the
// OperationCanceledException catch into the generic catch logging "Failed to retry startup"
// / "Failed to restart" and masks the real exit reason (exactly what obscured Theo's v1.3.2
// triage).
//
// This self-dispose path is deterministic with no timing dependency: empirically pinned in
// a scratch program (BUGGY -> ObjectDisposedException 3/3 at delay 0s and 1s; FIXED ->
// operation-cancelled 3/3). The test keeps the process in _processes so the closure's own
// stale?.Dispose() fires the disposer. Pre-fix the assertion fails with the logged
// ObjectDisposedException (pinning the actual bug, not a tautology); post-fix the captured
// token value survives the source's disposal and the cancelled token trips the
// cancellation handler (Debug level) -- green.
public class ProcessSupervisorRestartCancellationTests
{
    [Fact]
    public async Task OnStartupFailure_RetryClosureSelfDisposesProcess_DoesNotLogObjectDisposed()
    {
        var captureLogger = new CaptureLogger();
        var supervisor = CreateSupervisor(captureLogger);

        var process = new ManagedProcess(Ulid.NewUlid(), "startup-failer", "Startup Failer");

        // State the OnProcessExited dispatcher requires to route into OnStartupFailure.
        process.MarkStarting();

        // One backoff: StartupFailures == 1 -> GetStartupRetryDelay() == 1s, and
        // HasMaxStartupRetriesExceeded(3) is false, so the retry closure is scheduled.
        process.MarkBackoff(exitCode: 134);

        // Process stays in _processes: the retry closure's own TryRemove(appId)/stale.Dispose()
        // disposes this very process (and its CTS) -- the production self-dispose race.
        InjectProcess(supervisor, process);

        InvokePrivate(supervisor, "OnStartupFailure", process.AppId, process, 134);

        await WaitForClosureToSettleAsync(() => captureLogger.ErrorCount > 0 || captureLogger.SawRetryCancelledDebug);

        AssertNoDisposedRace
        (
            captureLogger.LoggedException(typeof(ObjectDisposedException)),
            captureLogger.DescribeErrors()
        );
    }

    [Fact]
    public async Task OnStartupFailure_OperatorStopRacesPendingRetry_DoesNotLogObjectDisposed()
    {
        var captureLogger = new CaptureLogger();
        var supervisor = CreateSupervisor(captureLogger);

        var process = new ManagedProcess(Ulid.NewUlid(), "startup-failer-stop", "Startup Failer Stop");

        process.MarkStarting();
        process.MarkBackoff(exitCode: 134);

        InjectProcess(supervisor, process);

        InvokePrivate(supervisor, "OnStartupFailure", process.AppId, process, 134);

        // Operator-stop path: StopAsync's cancel-before-loop sweep also disposes the pending
        // restart CTS. Belt-and-suspenders alongside the self-dispose test -- the closure
        // must never surface ObjectDisposedException regardless of which disposer wins.
        await supervisor.StopAsync(CancellationToken.None);

        await WaitForClosureToSettleAsync(() => captureLogger.ErrorCount > 0 || captureLogger.SawRetryCancelledDebug);

        AssertNoDisposedRace
        (
            captureLogger.LoggedException(typeof(ObjectDisposedException)),
            captureLogger.DescribeErrors()
        );
    }

    [Fact]
    public async Task OnRuntimeCrash_RestartClosureSelfDisposesProcess_DoesNotLogObjectDisposed()
    {
        var captureLogger = new CaptureLogger();
        var supervisor = CreateSupervisor(captureLogger);

        var appId = Ulid.NewUlid();
        var process = new ManagedProcess(appId, "crash-restarter", "Crash Restarter");

        // OnProcessExited routes a non-Starting exit into OnRuntimeCrash. Restart policy
        // OnCrash makes shouldRestart true; MarkCrashed(134) sets _consecutiveFailures == 1
        // so HasMaxRestartsExceeded() is false and GetBackoffDelay() == 1s -> the restart
        // closure is scheduled.
        SetRestartPolicy(supervisor, appId, RestartPolicy.OnCrash);

        process.MarkRunning(new InertProcessHandle());
        process.MarkCrashed(exitCode: 134);

        InjectProcess(supervisor, process);

        InvokePrivate(supervisor, "OnRuntimeCrash", appId, process, 134);

        await WaitForClosureToSettleAsync(() => captureLogger.ErrorCount > 0 || captureLogger.SawRetryCancelledDebug);

        AssertNoDisposedRace
        (
            captureLogger.LoggedException(typeof(ObjectDisposedException)),
            captureLogger.DescribeErrors()
        );
    }

    [Fact]
    public async Task OnRuntimeCrash_OperatorStopRacesPendingRestart_DoesNotLogObjectDisposed()
    {
        var captureLogger = new CaptureLogger();
        var supervisor = CreateSupervisor(captureLogger);

        var appId = Ulid.NewUlid();
        var process = new ManagedProcess(appId, "crash-restarter-stop", "Crash Restarter Stop");

        SetRestartPolicy(supervisor, appId, RestartPolicy.OnCrash);

        process.MarkRunning(new InertProcessHandle());
        process.MarkCrashed(exitCode: 134);

        InjectProcess(supervisor, process);

        InvokePrivate(supervisor, "OnRuntimeCrash", appId, process, 134);

        await supervisor.StopAsync(CancellationToken.None);

        await WaitForClosureToSettleAsync(() => captureLogger.ErrorCount > 0 || captureLogger.SawRetryCancelledDebug);

        AssertNoDisposedRace
        (
            captureLogger.LoggedException(typeof(ObjectDisposedException)),
            captureLogger.DescribeErrors()
        );
    }

    // The load-bearing, non-tautological discriminator (#312): no ObjectDisposedException
    // logged. Pre-fix the closure re-reads cancellation.Token on the disposed source ->
    // ObjectDisposedException -> generic catch logs it at Error level -> this fails.
    // Post-fix the captured token *value* survives the source's disposal, so no
    // ObjectDisposedException occurs -- the cancelled token is handled by the cancellation
    // path (Debug only). Note: the message string "Failed to retry startup" / "Failed to
    // restart" is deliberately NOT asserted on -- in the fixed path the closure proceeds
    // into StartAppInternalAsync, which fails on this test's intentionally-throwing DB
    // factory and logs that same message for a reason unrelated to the disposed-CTS race.
    // Keying on the exception type, not the message, keeps the assertion pinned to the
    // actual defect. Takes primitives, not the file-local CaptureLogger (CS9051).
    private static void AssertNoDisposedRace(bool loggedObjectDisposed, string errorSummary) =>
        loggedObjectDisposed.ShouldBeFalse
        (
            "The retry/restart closure must not throw ObjectDisposedException on the disposed "
            + "CTS (#312 disposed-CancellationTokenSource race). Captured errors: " + errorSummary
        );

    // The retry/restart closure is fire-and-forget. It terminates by logging exactly one of:
    // an Error (pre-fix ObjectDisposedException, or an unrelated retry failure on the
    // throwing DB factory) or the Debug "cancelled" line (post-fix cancelled-token path).
    // Poll for either; the bounded ceiling only stops a regression from hanging the suite.
    // The retry delay is 1s, so settle is observed in ~1-2s on either branch. Takes a
    // Func<bool> -- no file-local type in the signature (CS9051).
    private static async Task WaitForClosureToSettleAsync(Func<bool> settled)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);

        while (DateTime.UtcNow < deadline && !settled())
        {
            await Task.Delay(25);
        }
    }

    private static ProcessSupervisor CreateSupervisor(ILogger<ProcessSupervisor> logger)
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
            activityEventStore,
            logger
        );
    }

    // OnStartupFailure / OnRuntimeCrash are private -- the same reflection seam the sibling
    // ProcessSupervisorStopAsyncTests uses for _processes. Driving them directly exercises
    // the exact retry-scheduling code path without standing up a DB-backed App and the full
    // capability-resolution chain.
    private static void InvokePrivate(ProcessSupervisor supervisor, string methodName, Ulid appId, ManagedProcess process, int exitCode)
    {
        var method = typeof(ProcessSupervisor)
            .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Could not find " + methodName + " on ProcessSupervisor.");

        try
        {
            method.Invoke(supervisor, [appId, process, exitCode]);
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            throw tie.InnerException;
        }
    }

    private static void SetRestartPolicy(ProcessSupervisor supervisor, Ulid appId, RestartPolicy policy)
    {
        var policiesField = typeof(ProcessSupervisor)
            .GetField("_restartPolicies", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Could not find _restartPolicies field on ProcessSupervisor.");

        var dictionary = policiesField.GetValue(supervisor)
            ?? throw new InvalidOperationException("_restartPolicies was null on ProcessSupervisor instance.");

        var indexer = dictionary.GetType().GetProperty("Item")
            ?? throw new InvalidOperationException("ConcurrentDictionary indexer not found.");

        indexer.SetValue(dictionary, policy, [appId]);
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
}

// Captures Error-level logs (the bug's surface: LogError with the masking message and the
// ObjectDisposedException) and the post-fix Debug "cancelled" line so the fire-and-forget
// closure's terminal state is observable without timing assumptions.
file sealed class CaptureLogger : ILogger<ProcessSupervisor>
{
    private readonly Lock _gate = new();
    private readonly List<string> _errorMessages = [];
    private readonly List<Exception> _errorExceptions = [];

    // Monotonic true-only latch. The write happens under _gate in Log; the read is a
    // lock-free atomic bool load, which is correct for a set-once latch.
    public bool SawRetryCancelledDebug { get; private set; }

    public IReadOnlyList<string> ErrorMessages
    {
        get
        {
            lock (_gate)
            {
                return [.. _errorMessages];
            }
        }
    }

    public int ErrorCount
    {
        get
        {
            lock (_gate)
            {
                return _errorMessages.Count;
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>
    (
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        var message = formatter(state, exception);

        lock (_gate)
        {
            if (logLevel == LogLevel.Error)
            {
                _errorMessages.Add(message);

                if (exception is not null)
                {
                    _errorExceptions.Add(exception);
                }
            }
            else if (logLevel == LogLevel.Debug
                && (message.Contains("Startup retry cancelled", StringComparison.Ordinal)
                    || message.Contains("Restart cancelled", StringComparison.Ordinal)))
            {
                SawRetryCancelledDebug = true;
            }
        }
    }

    public bool LoggedException(Type exceptionType)
    {
        lock (_gate)
        {
            return _errorExceptions.Exists(exceptionType.IsInstanceOfType);
        }
    }

    public string DescribeErrors()
    {
        lock (_gate)
        {
            return _errorMessages.Count == 0
                ? "(none)"
                : string.Join(" | ", _errorMessages);
        }
    }
}

// Sealed: file-scoped test fake. Inert handle so MarkRunning has a handle to capture; the
// process never actually runs, and Exited is never raised (the test drives OnRuntimeCrash
// directly rather than via the Exited event).
file sealed class InertProcessHandle : IProcessHandle
{
    public int Pid => 7777;

    public bool HasExited => true;

    public int? ExitCode => 134;

#pragma warning disable CS0067 // Event is part of the IProcessHandle contract; not raised in this fake
    public event Action<int>? Exited;
#pragma warning restore CS0067

    public bool TryGracefulShutdown() => false;

    public void Kill() { }

    public void Dispose() { }
}

file sealed class UnusedRunner : IManagedProcessRunner
{
    public IProcessHandle Start(ProcessStartConfiguration configuration) =>
        throw new NotSupportedException("Runner.Start should not be invoked in restart-cancellation tests.");

    public Task<ProcessRunResult> RunToCompletionAsync
    (
        ProcessStartConfiguration configuration,
        TimeSpan timeout,
        CancellationToken ct = default
    ) =>
        throw new NotSupportedException("Runner.RunToCompletionAsync should not be invoked in restart-cancellation tests.");
}

// AppStore.GetByIdAsync asks for a DbContext on a cache miss. Throwing keeps the test honest:
// the retry closure's config-resolution path is exercised but cannot succeed, so the only
// way the closure can throw ObjectDisposedException is the disposed-CTS race under test.
file sealed class ThrowingDbContextFactory : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() =>
        throw new NotSupportedException("DB access should not occur in restart-cancellation tests.");

#pragma warning disable VSTHRD200 // Async method naming -- interface implementation
    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("DB access should not occur in restart-cancellation tests.");
#pragma warning restore VSTHRD200
}
