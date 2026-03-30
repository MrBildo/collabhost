namespace Collabhost.Api.Services;

public record ProcessStateChangedEvent
(
    Guid AppId,
    string AppExternalId,
    Guid PreviousStateId,
    Guid NewStateId
);

public interface IProcessStateEventBus
{
    void Publish(ProcessStateChangedEvent processStateChangedEvent);

    IDisposable Subscribe(Action<ProcessStateChangedEvent> handler);
}

public sealed class ProcessStateEventBus(ILogger<ProcessStateEventBus> logger) : IProcessStateEventBus
{
    private readonly ILogger<ProcessStateEventBus> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly Lock _subscriberLock = new();
    private readonly List<Action<ProcessStateChangedEvent>> _subscribers = [];

    public void Publish(ProcessStateChangedEvent processStateChangedEvent)
    {
        ArgumentNullException.ThrowIfNull(processStateChangedEvent);

        Action<ProcessStateChangedEvent>[] snapshot;
        lock (_subscriberLock)
        {
            snapshot = [.. _subscribers];
        }

        foreach (var handler in snapshot)
        {
            try
            {
                handler(processStateChangedEvent);
            }
            catch (Exception exception)
            {
                _logger.LogError
                (
                    exception,
                    "Subscriber threw an exception handling ProcessStateChangedEvent for app {AppExternalId}",
                    processStateChangedEvent.AppExternalId
                );
            }
        }
    }

    public IDisposable Subscribe(Action<ProcessStateChangedEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_subscriberLock)
        {
            _subscribers.Add(handler);
        }

        return new Subscription(this, handler);
    }

    private void Unsubscribe(Action<ProcessStateChangedEvent> handler)
    {
        lock (_subscriberLock)
        {
            _subscribers.Remove(handler);
        }
    }

    private sealed class Subscription(ProcessStateEventBus bus, Action<ProcessStateChangedEvent> handler) : IDisposable
    {
        private readonly ProcessStateEventBus _bus = bus;
        private readonly Action<ProcessStateChangedEvent> _handler = handler;
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _bus.Unsubscribe(_handler);
                _disposed = true;
            }
        }
    }
}
