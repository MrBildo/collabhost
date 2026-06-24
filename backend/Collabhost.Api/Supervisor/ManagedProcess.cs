using Collabhost.Api.Registry;
using Collabhost.Api.Supervisor.Containment;

namespace Collabhost.Api.Supervisor;

public class ManagedProcess(Ulid appId, string appSlug, string displayName) : IDisposable
{
    // SUP-16: every state field below is mutated from multiple threads (operator
    // request threads, the crash-restart Task.Run closures, the grace-period closure,
    // the synchronous Exited callback) and read concurrently by request threads that
    // assemble the app-detail response. Each MarkXxx writes a GROUP of related fields
    // as one logical transition; readers want a coherent (State, Pid, Port) view, not a
    // half-applied one. The invariant is multi-field coherence, not a single non-torn
    // read -- so the primitive is a lock around every write-group and every getter, not
    // per-field volatile (volatile would make each field's read non-torn but still let a
    // reader stitch State==Running from one writer with Pid==null from another).
    private readonly Lock _stateLock = new();

    private IProcessHandle? _handle;
    private IContainmentHandle? _containmentHandle;
    private int _consecutiveFailures;
    private DateTime? _lastHealthyAt;
    private CancellationTokenSource? _restartDelayCancellation;

    // IDE0032 (use auto property) is a false positive here: these are the SUP-16 state-coherence
    // backing fields. Each is read through a _stateLock-guarded getter and written inside a
    // _stateLock-guarded MarkXxx write-group -- an auto-property would expose the torn read this
    // lock exists to prevent. The explicit field + locked accessor is the fix, not a smell.
#pragma warning disable IDE0032 // Use auto property
    private ProcessState _state = ProcessState.Stopped;
    private int? _pid;
    private int? _port;
    private DateTime? _startedAt;
    private int _restartCount;
    private DateTime? _lastRestartAt;
    private bool _stoppedByOperator;
    private int? _lastExitCode;
    private DateTime? _lastExitAt;
    private int _startupFailures;
#pragma warning restore IDE0032 // Use auto property

    public Ulid AppId { get; } = appId;

    public string AppSlug { get; } = appSlug;

    public string DisplayName { get; } = displayName;

    public ProcessState State
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
    }

    public int? Pid
    {
        get
        {
            lock (_stateLock)
            {
                return _pid;
            }
        }
    }

    public int? Port
    {
        get
        {
            lock (_stateLock)
            {
                return _port;
            }
        }
    }

    public DateTime? StartedAt
    {
        get
        {
            lock (_stateLock)
            {
                return _startedAt;
            }
        }
    }

    public int RestartCount
    {
        get
        {
            lock (_stateLock)
            {
                return _restartCount;
            }
        }
    }

    public DateTime? LastRestartAt
    {
        get
        {
            lock (_stateLock)
            {
                return _lastRestartAt;
            }
        }
    }

    public bool StoppedByOperator
    {
        get
        {
            lock (_stateLock)
            {
                return _stoppedByOperator;
            }
        }
    }

    public int? LastExitCode
    {
        get
        {
            lock (_stateLock)
            {
                return _lastExitCode;
            }
        }
    }

    public DateTime? LastExitAt
    {
        get
        {
            lock (_stateLock)
            {
                return _lastExitAt;
            }
        }
    }

    public int StartupFailures
    {
        get
        {
            lock (_stateLock)
            {
                return _startupFailures;
            }
        }
    }

    public bool IsRunning => State == ProcessState.Running;

    public bool IsStopped => State == ProcessState.Stopped;

    public bool IsCrashed => State == ProcessState.Crashed;

    public bool IsRestarting => State == ProcessState.Restarting;

    public bool IsBackoff => State == ProcessState.Backoff;

    public bool IsFatal => State == ProcessState.Fatal;

    public double? UptimeSeconds
    {
        get
        {
            lock (_stateLock)
            {
                return _startedAt.HasValue && _state == ProcessState.Running
                    ? (DateTime.UtcNow - _startedAt.Value).TotalSeconds
                    : null;
            }
        }
    }

    public bool HasProcessExited => _handle?.HasExited ?? true;

    public int? HandleExitCode => _handle?.ExitCode;

    // Coherent read of the fields a status reader assembles together. Reading them under
    // the same lock that guards the writers is what makes the running-process invariant
    // (Running => Pid and Port are set) hold against a concurrent transition. (SUP-16)
    //
    // UptimeSeconds is computed inside the same lock (Card #428) -- it is part of the
    // coherent moment a detail reader assembles, not a separate property load. Reading
    // it under the lock keeps "status: running" from stitching with "uptime: null" when
    // a stop lands between the snapshot and a follow-up property read. Mirrors the
    // UptimeSeconds property: live only while Running with a recorded start time.
    public ProcessStateSnapshot ReadSnapshot()
    {
        lock (_stateLock)
        {
            var uptimeSeconds = _startedAt.HasValue && _state == ProcessState.Running
                ? (DateTime.UtcNow - _startedAt.Value).TotalSeconds
                : (double?)null;

            return new ProcessStateSnapshot(_state, _pid, _port, uptimeSeconds);
        }
    }

    public void AssignPort(int port)
    {
        lock (_stateLock)
        {
            _port = port;
        }
    }

    public void MarkStoppedByOperator()
    {
        lock (_stateLock)
        {
            _stoppedByOperator = true;
        }
    }

    public void ClearStoppedByOperator()
    {
        lock (_stateLock)
        {
            _stoppedByOperator = false;
        }
    }

    public void SetContainmentHandle(IContainmentHandle? handle) => _containmentHandle = handle;

    public void SetHandle(IProcessHandle handle)
    {
        lock (_stateLock)
        {
            _handle = handle;
            _pid = handle.Pid;
        }
    }

    public ProcessState MarkStarting()
    {
        lock (_stateLock)
        {
            var previous = _state;
            _state = ProcessState.Starting;
            return previous;
        }
    }

    public ProcessState MarkRunning(IProcessHandle handle)
    {
        lock (_stateLock)
        {
            var previous = _state;

            _handle = handle;
            _pid = handle.Pid;
            _startedAt = DateTime.UtcNow;
            _state = ProcessState.Running;
            _lastHealthyAt = DateTime.UtcNow;
            _startupFailures = 0;

            return previous;
        }
    }

    public ProcessState MarkRunning()
    {
        lock (_stateLock)
        {
            var previous = _state;

            _startedAt = DateTime.UtcNow;
            _state = ProcessState.Running;
            _lastHealthyAt = DateTime.UtcNow;
            _startupFailures = 0;

            return previous;
        }
    }

    public ProcessState MarkStopping()
    {
        lock (_stateLock)
        {
            var previous = _state;
            _state = ProcessState.Stopping;
            CancelPendingRestartCore();
            return previous;
        }
    }

    public ProcessState MarkStopped()
    {
        lock (_stateLock)
        {
            var previous = _state;

            _state = ProcessState.Stopped;
            _pid = null;
            _port = null;
            _startedAt = null;
            _consecutiveFailures = 0;
            _startupFailures = 0;

            CancelPendingRestartCore();

            return previous;
        }
    }

    public ProcessState MarkCrashed(int exitCode)
    {
        lock (_stateLock)
        {
            var previous = _state;

            _state = ProcessState.Crashed;
            _consecutiveFailures++;
            _lastExitCode = exitCode;
            _lastExitAt = DateTime.UtcNow;
            _pid = null;
            _port = null;
            _startedAt = null;

            return previous;
        }
    }

    public ProcessState MarkBackoff(int exitCode)
    {
        lock (_stateLock)
        {
            var previous = _state;

            _state = ProcessState.Backoff;
            _startupFailures++;
            _lastExitCode = exitCode;
            _lastExitAt = DateTime.UtcNow;

            return previous;
        }
    }

    public ProcessState MarkFatal()
    {
        lock (_stateLock)
        {
            var previous = _state;
            _state = ProcessState.Fatal;
            return previous;
        }
    }

    public ProcessState MarkRestarting()
    {
        lock (_stateLock)
        {
            var previous = _state;

            _state = ProcessState.Restarting;
            _restartCount++;
            _lastRestartAt = DateTime.UtcNow;

            return previous;
        }
    }

    public TimeSpan GetBackoffDelay()
    {
        lock (_stateLock)
        {
            var delay = Math.Min(Math.Pow(2, _consecutiveFailures - 1), 60);
            return TimeSpan.FromSeconds(delay);
        }
    }

    public bool ShouldResetRestartCount()
    {
        lock (_stateLock)
        {
            return _lastHealthyAt.HasValue
                && _state == ProcessState.Running
                && (DateTime.UtcNow - _lastHealthyAt.Value).TotalSeconds >= 300;
        }
    }

    public void ResetRestartCount()
    {
        lock (_stateLock)
        {
            _consecutiveFailures = 0;
            _lastHealthyAt = DateTime.UtcNow;
        }
    }

    public bool HasMaxRestartsExceeded(int maxRestarts = 10)
    {
        lock (_stateLock)
        {
            return _consecutiveFailures >= maxRestarts;
        }
    }

    public bool HasMaxStartupRetriesExceeded(int max)
    {
        lock (_stateLock)
        {
            return _startupFailures >= max;
        }
    }

    public TimeSpan GetStartupRetryDelay()
    {
        lock (_stateLock)
        {
            return TimeSpan.FromSeconds(_startupFailures);
        }
    }

    public void SetRestartDelayCancellation(CancellationTokenSource cancellation)
    {
        lock (_stateLock)
        {
            _restartDelayCancellation = cancellation;
        }
    }

    public void CancelPendingRestart()
    {
        lock (_stateLock)
        {
            CancelPendingRestartCore();
        }
    }

    // Must be called with _stateLock held -- the public entry points and the MarkXxx
    // transitions that clear a pending restart all hold the lock when they invoke this.
    private void CancelPendingRestartCore()
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
        GC.SuppressFinalize(this);
    }
}

public record ProcessStateSnapshot(ProcessState State, int? Pid, int? Port, double? UptimeSeconds);
