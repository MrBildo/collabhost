using Collabhost.Api.Events;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Events;

public class EventBusTests
{
    // Test-only record, sealed to satisfy MA0053
    private sealed record TestEvent(string Message);

    [Fact]
    public void Publish_NotifiesSubscriber()
    {
        var bus = new EventBus<TestEvent>();
        TestEvent? received = null;

        bus.Subscribe(e => received = e);

        bus.Publish(new TestEvent("hello"));

        received.ShouldNotBeNull();
        received.Message.ShouldBe("hello");
    }

    [Fact]
    public void Publish_NotifiesMultipleSubscribers()
    {
        var bus = new EventBus<TestEvent>();
        var count = 0;

        bus.Subscribe(_ => count++);
        bus.Subscribe(_ => count++);
        bus.Subscribe(_ => count++);

        bus.Publish(new TestEvent("hello"));

        count.ShouldBe(3);
    }

    [Fact]
    public void Unsubscribe_StopsNotifications()
    {
        var bus = new EventBus<TestEvent>();
        var count = 0;

        var subscription = bus.Subscribe(_ => count++);

        bus.Publish(new TestEvent("first"));
        count.ShouldBe(1);

        subscription.Dispose();

        bus.Publish(new TestEvent("second"));
        count.ShouldBe(1);
    }

    [Fact]
    public void Publish_SubscriberThrows_OtherSubscribersStillNotified()
    {
        var bus = new EventBus<TestEvent>();
        var count = 0;

        bus.Subscribe(_ => throw new InvalidOperationException("boom"));
        bus.Subscribe(_ => count++);

        bus.Publish(new TestEvent("hello"));

        count.ShouldBe(1);
    }

    [Fact]
    public void Publish_NoSubscribers_DoesNotThrow() =>
        Should.NotThrow(() => new EventBus<TestEvent>().Publish(new TestEvent("hello")));

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var bus = new EventBus<TestEvent>();
        var subscription = bus.Subscribe(_ => { });

        subscription.Dispose();

        Should.NotThrow(() => subscription.Dispose());
    }
}
