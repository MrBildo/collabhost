namespace Collabhost.Api.Events;

public class EventBus<T> : IEventBus<T>
{
    private readonly List<Action<T>> _handlers = [];
    private readonly Lock _lock = new();

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
            catch
            {
                // One subscriber's failure must not block others
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
