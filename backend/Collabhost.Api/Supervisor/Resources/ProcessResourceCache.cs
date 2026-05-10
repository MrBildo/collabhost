using System.Collections.Concurrent;

namespace Collabhost.Api.Supervisor.Resources;

// Reader interface consumed by AppEndpoints. The orchestrator hosted service writes
// snapshots into this cache on its tick; endpoints read on demand and pay zero per-
// request cost. A null return means "no snapshot yet" -- either the process has not
// been sampled, the process is not running, or sampling failed for this PID.
public interface IProcessResourceCache
{
    ProcessResourceSnapshot? GetLatest(Ulid appId);
}

public class ProcessResourceCache : IProcessResourceCache
{
    private readonly ConcurrentDictionary<Ulid, ProcessResourceSnapshot> _latest = new();

    public ProcessResourceSnapshot? GetLatest(Ulid appId) =>
        _latest.TryGetValue(appId, out var snapshot) ? snapshot : null;

    public void Set(Ulid appId, ProcessResourceSnapshot snapshot) =>
        _latest[appId] = snapshot;

    public void Remove(Ulid appId) =>
        _latest.TryRemove(appId, out _);
}
