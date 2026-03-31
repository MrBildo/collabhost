using Collabhost.Api.Domain.Catalogs;

namespace Collabhost.Api.Services;

public class ManagedProcess(Guid appId, string appExternalId, string appName) : IDisposable
{
    public Guid AppId { get; } = appId;
    public string AppExternalId { get; } = appExternalId;
    public string AppName { get; } = appName;
    public Guid ProcessStateId { get; private set; } = IdentifierCatalog.ProcessStates.Stopped;
    public int? Pid { get; private set; }
    public int? Port { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public int RestartCount { get; private set; }
    public DateTime? LastRestartAt { get; private set; }
    public bool StoppedByOperator { get; private set; }

    private IProcessHandle? _handle;
    private int _consecutiveFailures;
    private DateTime? _lastHealthyAt;
    private CancellationTokenSource? _restartDelayCancellation;

    public RingBuffer<LogEntry> LogBuffer { get; } = new(1000);

    public bool IsRunning => ProcessStateId == IdentifierCatalog.ProcessStates.Running;
    public bool IsStopped => ProcessStateId == IdentifierCatalog.ProcessStates.Stopped;
    public bool IsCrashed => ProcessStateId == IdentifierCatalog.ProcessStates.Crashed;
    public bool IsRestarting => ProcessStateId == IdentifierCatalog.ProcessStates.Restarting;

    public double? UptimeSeconds => StartedAt.HasValue && IsRunning
        ? (DateTime.UtcNow - StartedAt.Value).TotalSeconds
        : null;

    public void AssignPort(int port) => Port = port;

    public void MarkStoppedByOperator() => StoppedByOperator = true;

    public void ClearStoppedByOperator() => StoppedByOperator = false;

    public void MarkStarting() => ProcessStateId = IdentifierCatalog.ProcessStates.Starting;

    public void MarkRunning(IProcessHandle handle)
    {
        _handle = handle;
        Pid = handle.Pid;
        StartedAt = DateTime.UtcNow;
        ProcessStateId = IdentifierCatalog.ProcessStates.Running;
        _lastHealthyAt = DateTime.UtcNow;
    }

    public void MarkStopping()
    {
        ProcessStateId = IdentifierCatalog.ProcessStates.Stopping;
        CancelPendingRestart();
    }

    public void MarkStopped()
    {
        ProcessStateId = IdentifierCatalog.ProcessStates.Stopped;
        Pid = null;
        Port = null;
        StartedAt = null;
        _consecutiveFailures = 0;
        CancelPendingRestart();
    }

    public void MarkCrashed()
    {
        ProcessStateId = IdentifierCatalog.ProcessStates.Crashed;
        _consecutiveFailures++;
        Pid = null;
        Port = null;
        StartedAt = null;
    }

    public void MarkRestarting()
    {
        ProcessStateId = IdentifierCatalog.ProcessStates.Restarting;
        RestartCount++;
        LastRestartAt = DateTime.UtcNow;
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

    public void SetRestartDelayCancellation(CancellationTokenSource cancellation) => _restartDelayCancellation = cancellation;

    public void CancelPendingRestart()
    {
        _restartDelayCancellation?.Cancel();
        _restartDelayCancellation?.Dispose();
        _restartDelayCancellation = null;
    }

    public void KillProcess() => _handle?.Kill();

    public void Dispose()
    {
        CancelPendingRestart();
        _handle?.Dispose();
        GC.SuppressFinalize(this);
    }
}
