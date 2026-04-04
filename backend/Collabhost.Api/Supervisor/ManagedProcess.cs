using Collabhost.Api.Registry;
using Collabhost.Api.Shared;
using Collabhost.Api.Supervisor.Containment;

namespace Collabhost.Api.Supervisor;

public class ManagedProcess(Ulid appId, string appSlug, string displayName) : IDisposable
{
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    private IProcessHandle? _handle;
    private IContainmentHandle? _containmentHandle;
    private int _consecutiveFailures;
    private DateTime? _lastHealthyAt;
    private CancellationTokenSource? _restartDelayCancellation;

    public Ulid AppId { get; } = appId;

    public string AppSlug { get; } = appSlug;

    public string DisplayName { get; } = displayName;

    public ProcessState State { get; private set; } = ProcessState.Stopped;

    public int? Pid { get; private set; }

    public int? Port { get; private set; }

    public DateTime? StartedAt { get; private set; }

    public int RestartCount { get; private set; }

    public DateTime? LastRestartAt { get; private set; }

    public bool StoppedByOperator { get; private set; }

    public int? LastExitCode { get; private set; }

    public DateTime? LastExitAt { get; private set; }

    public int StartupFailures { get; private set; }

    public RingBuffer<LogEntry> LogBuffer { get; } = new(1000);

    public bool IsRunning => State == ProcessState.Running;

    public bool IsStopped => State == ProcessState.Stopped;

    public bool IsCrashed => State == ProcessState.Crashed;

    public bool IsRestarting => State == ProcessState.Restarting;

    public bool IsBackoff => State == ProcessState.Backoff;

    public bool IsFatal => State == ProcessState.Fatal;

    public double? UptimeSeconds => StartedAt.HasValue && IsRunning
        ? (DateTime.UtcNow - StartedAt.Value).TotalSeconds
        : null;

    public bool HasProcessExited => _handle?.HasExited ?? true;

    public async Task<IAsyncDisposable> AcquireOperationLockAsync(CancellationToken ct = default)
    {
        await _operationLock.WaitAsync(ct);

        return new OperationLockRelease(_operationLock);
    }

    public void AssignPort(int port) => Port = port;

    public void MarkStoppedByOperator() => StoppedByOperator = true;

    public void ClearStoppedByOperator() => StoppedByOperator = false;

    public void SetContainmentHandle(IContainmentHandle? handle) => _containmentHandle = handle;

    public void SetHandle(IProcessHandle handle)
    {
        _handle = handle;
        Pid = handle.Pid;
    }

    public ProcessState MarkStarting()
    {
        var previous = State;
        State = ProcessState.Starting;
        return previous;
    }

    public ProcessState MarkRunning(IProcessHandle handle)
    {
        var previous = State;

        _handle = handle;
        Pid = handle.Pid;
        StartedAt = DateTime.UtcNow;
        State = ProcessState.Running;
        _lastHealthyAt = DateTime.UtcNow;
        StartupFailures = 0;

        return previous;
    }

    public ProcessState MarkRunning()
    {
        var previous = State;

        StartedAt = DateTime.UtcNow;
        State = ProcessState.Running;
        _lastHealthyAt = DateTime.UtcNow;
        StartupFailures = 0;

        return previous;
    }

    public ProcessState MarkStopping()
    {
        var previous = State;
        State = ProcessState.Stopping;
        CancelPendingRestart();
        return previous;
    }

    public ProcessState MarkStopped()
    {
        var previous = State;

        State = ProcessState.Stopped;
        Pid = null;
        Port = null;
        StartedAt = null;
        _consecutiveFailures = 0;
        StartupFailures = 0;

        CancelPendingRestart();

        return previous;
    }

    public ProcessState MarkCrashed(int exitCode)
    {
        var previous = State;

        State = ProcessState.Crashed;
        _consecutiveFailures++;
        LastExitCode = exitCode;
        LastExitAt = DateTime.UtcNow;
        Pid = null;
        Port = null;
        StartedAt = null;

        return previous;
    }

    public ProcessState MarkBackoff(int exitCode)
    {
        var previous = State;

        State = ProcessState.Backoff;
        StartupFailures++;
        LastExitCode = exitCode;
        LastExitAt = DateTime.UtcNow;

        return previous;
    }

    public ProcessState MarkFatal()
    {
        var previous = State;
        State = ProcessState.Fatal;
        return previous;
    }

    public ProcessState MarkRestarting()
    {
        var previous = State;

        State = ProcessState.Restarting;
        RestartCount++;
        LastRestartAt = DateTime.UtcNow;

        return previous;
    }

    public TimeSpan GetBackoffDelay()
    {
        var delay = Math.Min(Math.Pow(2, _consecutiveFailures - 1), 60);
        return TimeSpan.FromSeconds(delay);
    }

    public bool ShouldResetRestartCount() =>
        _lastHealthyAt.HasValue
        && IsRunning
        && (DateTime.UtcNow - _lastHealthyAt.Value).TotalSeconds >= 300;

    public void ResetRestartCount()
    {
        _consecutiveFailures = 0;
        _lastHealthyAt = DateTime.UtcNow;
    }

    public bool HasMaxRestartsExceeded(int maxRestarts = 10) =>
        _consecutiveFailures >= maxRestarts;

    public bool HasMaxStartupRetriesExceeded(int max) =>
        StartupFailures >= max;

    public TimeSpan GetStartupRetryDelay() =>
        TimeSpan.FromSeconds(StartupFailures);

    public void SetRestartDelayCancellation(CancellationTokenSource cancellation) =>
        _restartDelayCancellation = cancellation;

    public void CancelPendingRestart()
    {
        _restartDelayCancellation?.Cancel();
        _restartDelayCancellation?.Dispose();
        _restartDelayCancellation = null;
    }

    public bool TryGracefulShutdown() => _handle?.TryGracefulShutdown() ?? false;

    public void KillProcess() => _handle?.Kill();

    public void Dispose()
    {
        CancelPendingRestart();
        _handle?.Dispose();
        _containmentHandle?.Dispose();
        _operationLock.Dispose();
        GC.SuppressFinalize(this);
    }
}

file record struct OperationLockRelease(SemaphoreSlim Semaphore) : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        Semaphore.Release();

        return ValueTask.CompletedTask;
    }
}
