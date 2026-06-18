using System.Collections.Concurrent;

using Collabhost.Api.Shared;

namespace Collabhost.Api.Supervisor;

// Per-app stdout/stderr ring-buffer registry. Each managed process captures its
// output into a bounded RingBuffer keyed by app id; the log snapshot and SSE
// log-stream endpoints read the same buffer. Owned by ProcessSupervisor, which
// creates a buffer at spawn, removes it when an app is deleted, and clears all
// buffers on host shutdown.
public class ProcessLogBufferStore
{
    // Bounded so a long-running, chatty process cannot grow log memory without
    // limit -- the buffer keeps the most-recent N entries and the SSE stream
    // backfills from it on connect.
    private const int _bufferCapacity = 1000;

    private readonly ConcurrentDictionary<Ulid, RingBuffer<LogEntry>> _buffers = new();

    public RingBuffer<LogEntry> GetOrCreate(Ulid appId) =>
        _buffers.GetOrAdd(appId, _ => new RingBuffer<LogEntry>(_bufferCapacity));

    public void Remove(Ulid appId) => _buffers.TryRemove(appId, out _);

    public void Clear() => _buffers.Clear();
}
