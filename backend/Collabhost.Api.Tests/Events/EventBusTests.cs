using Collabhost.Api.Events;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Events;

public class EventBusTests
{
    // Test-only record, sealed to satisfy MA0053
    private sealed record TestEvent(string Message);

    private static EventBus<TestEvent> CreateBus(ILogger<EventBus<TestEvent>>? logger = null) =>
        new(logger ?? NullLogger<EventBus<TestEvent>>.Instance);

    [Fact]
    public void Publish_NotifiesSubscriber()
    {
        var bus = CreateBus();
        TestEvent? received = null;

        bus.Subscribe(e => received = e);

        bus.Publish(new TestEvent("hello"));

        received.ShouldNotBeNull();
        received.Message.ShouldBe("hello");
    }

    [Fact]
    public void Publish_NotifiesMultipleSubscribers()
    {
        var bus = CreateBus();
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
        var bus = CreateBus();
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
        var bus = CreateBus();
        var count = 0;

        bus.Subscribe(_ => throw new InvalidOperationException("boom"));
        bus.Subscribe(_ => count++);

        bus.Publish(new TestEvent("hello"));

        count.ShouldBe(1);
    }

    [Fact]
    public void Publish_SubscriberThrows_LogsWarningCarryingTheException()
    {
        var capture = new CapturingLogger<EventBus<TestEvent>>();
        var bus = CreateBus(capture);
        var boom = new InvalidOperationException("boom");

        bus.Subscribe(_ => throw boom);

        bus.Publish(new TestEvent("hello"));

        capture.ShouldHaveLogged(LogLevel.Warning, boom);
    }

    [Fact]
    public void Publish_NoSubscribers_DoesNotThrow() =>
        Should.NotThrow(() => CreateBus().Publish(new TestEvent("hello")));

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var bus = CreateBus();
        var subscription = bus.Subscribe(_ => { });

        subscription.Dispose();

        Should.NotThrow(() => subscription.Dispose());
    }
}

// Captures ILogger calls so the swallowed-handler-failure assertion can inspect level +
// the exception object that reached the log. File-scoped: only EventBus tests need it.
file sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, Exception? Exception)> _entries = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>
    (
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    ) =>
        _entries.Add((logLevel, exception));

    public void ShouldHaveLogged(LogLevel level, Exception exception) =>
        _entries.ShouldContain
        (
            e => e.Level == level && ReferenceEquals(e.Exception, exception),
            $"Expected a log entry at {level} carrying the thrown exception but captured: "
                + $"{string.Join(" | ", _entries.Select(e => $"{e.Level}:{e.Exception?.GetType().Name ?? "none"}"))}"
        );
}
