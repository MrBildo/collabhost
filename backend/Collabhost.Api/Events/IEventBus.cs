namespace Collabhost.Api.Events;

public interface IEventBus<T>
{
    void Publish(T eventData);

    IDisposable Subscribe(Action<T> handler);
}
