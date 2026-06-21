using Microsoft.Extensions.Logging.Abstractions;

namespace Collabhost.Api.Events;

// logger is optional only so the many direct test constructions that don't exercise the
// swallowed-handler path need not thread a no-op logger. In production DI always supplies
// the registered ILogger<EventBus<T>>; the NullLogger fallback applies only to bare
// new EventBus<T>() in tests.
public class EventBus<T>(ILogger<EventBus<T>>? logger = null) : IEventBus<T>
{
    private readonly List<Action<T>> _handlers = [];
    private readonly Lock _lock = new();

    private readonly ILogger<EventBus<T>> _logger = logger ?? NullLogger<EventBus<T>>.Instance;

    public void Publish(T eventData)
    {
        List<Action<T>> snapshot;
        lock (_lock)
        {
            snapshot = [.. _handlers];
        }

        foreach (var handler in snapshot)
        {
            try
            {
                handler(eventData);
            }
            catch (Exception ex)
            {
                // One subscriber's failure must not block others. Isolation is the
                // contract; the log is the observability the isolation would otherwise
                // hide -- a throwing handler silently drops the event for that subscriber
                // (e.g. a proxy route-sync that never runs) without it.
                _logger.LogWarning
                (
                    ex,
                    "Event subscriber threw while handling {EventType}; other subscribers were unaffected.",
                    typeof(T).Name
                );
            }
        }
    }

    public IDisposable Subscribe(Action<T> handler)
    {
        lock (_lock)
        {
            _handlers.Add(handler);
        }

        return new Subscription(this, handler);
    }

    private void RemoveHandler(Action<T> handler)
    {
        lock (_lock)
        {
            _handlers.Remove(handler);
        }
    }

    // No subclasses expected -- private disposal token
    private sealed class Subscription(EventBus<T> bus, Action<T> handler) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                bus.RemoveHandler(handler);
                _disposed = true;
            }
        }
    }
}
