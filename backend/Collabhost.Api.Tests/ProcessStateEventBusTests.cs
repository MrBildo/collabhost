using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests;

public sealed class ProcessStateEventBusTests
{
    private readonly ProcessStateEventBus _bus = new(NullLogger<ProcessStateEventBus>.Instance);

    private static ProcessStateChangedEvent CreateTestEvent
    (
        Guid? previousStateId = null,
        Guid? newStateId = null
    ) =>
        new
        (
            Guid.NewGuid(),
            "01JTEST000000000000000TEST1",
            previousStateId ?? IdentifierCatalog.ProcessStates.Stopped,
            newStateId ?? IdentifierCatalog.ProcessStates.Starting
        );

    [Fact]
    public void Publish_DeliversToSubscriber()
    {
        // Arrange
        ProcessStateChangedEvent? received = null;
        _bus.Subscribe(e => received = e);

        var published = CreateTestEvent();

        // Act
        _bus.Publish(published);

        // Assert
        received.ShouldNotBeNull();
        received.ShouldBe(published);
    }

    [Fact]
    public void Publish_DeliversToMultipleSubscribers()
    {
        // Arrange
        var receivedEvents = new List<ProcessStateChangedEvent>();
        _bus.Subscribe(e => receivedEvents.Add(e));
        _bus.Subscribe(e => receivedEvents.Add(e));

        var published = CreateTestEvent();

        // Act
        _bus.Publish(published);

        // Assert
        receivedEvents.Count.ShouldBe(2);
        receivedEvents.ShouldAllBe(e => e == published);
    }

    [Fact]
    public void Unsubscribe_StopsReceivingEvents()
    {
        // Arrange
        var receivedCount = 0;
        var subscription = _bus.Subscribe(_ => receivedCount++);

        _bus.Publish(CreateTestEvent());
        receivedCount.ShouldBe(1);

        // Act
        subscription.Dispose();
        _bus.Publish(CreateTestEvent());

        // Assert
        receivedCount.ShouldBe(1);
    }

    [Fact]
    public void Publish_SubscriberException_DoesNotCrashPublisher()
    {
        // Arrange
        ProcessStateChangedEvent? secondReceived = null;
        _bus.Subscribe(_ => throw new InvalidOperationException("Subscriber failure"));
        _bus.Subscribe(e => secondReceived = e);

        var published = CreateTestEvent();

        // Act — should not throw
        _bus.Publish(published);

        // Assert — second subscriber still received the event
        secondReceived.ShouldNotBeNull();
        secondReceived.ShouldBe(published);
    }

    [Fact]
    public void Publish_SubscriberException_IsLogged()
    {
        // Arrange
        var loggerFactory = new TestLoggerFactory();
        var bus = new ProcessStateEventBus(loggerFactory.CreateLogger<ProcessStateEventBus>());

        bus.Subscribe(_ => throw new InvalidOperationException("Test subscriber failure"));

        // Act
        bus.Publish(CreateTestEvent());

        // Assert
        loggerFactory.LogEntries.ShouldContain(entry => entry.LogLevel == LogLevel.Error);
    }

    [Fact]
    public async Task Publish_ConcurrentPublishAndSubscribe_IsThreadSafe()
    {
        // Arrange
        var receivedCount = 0;
        var barrier = new ManualResetEventSlim(false);

        // Act — concurrent subscribes and publishes
        var tasks = new List<Task>();

        for (var i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                barrier.Wait();
                _bus.Subscribe(_ => Interlocked.Increment(ref receivedCount));
            }));
        }

        barrier.Set();
        await Task.WhenAll(tasks);

        // All 10 subscribers should be registered; publish should reach all
        _bus.Publish(CreateTestEvent());

        // Assert
        receivedCount.ShouldBe(10);
    }

    [Fact]
    public void Unsubscribe_DisposeTwice_DoesNotThrow()
    {
        // Arrange
        var subscription = _bus.Subscribe(_ => { });

        // Act & Assert — disposing twice should be safe
        subscription.Dispose();
        Should.NotThrow(() => subscription.Dispose());
    }

    [Fact]
    public void Publish_NoSubscribers_DoesNotThrow() =>
        Should.NotThrow(() => _bus.Publish(CreateTestEvent()));

    private sealed class TestLoggerFactory : ILoggerFactory
    {
        public List<TestLogEntry> LogEntries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new TestLogger(this);

        public void AddProvider(ILoggerProvider provider) { }

        public void Dispose() => GC.SuppressFinalize(this);

        public void AddEntry(TestLogEntry entry) => LogEntries.Add(entry);
    }

    private sealed class TestLogger(TestLoggerFactory factory) : ILogger<ProcessStateEventBus>
    {
        private readonly TestLoggerFactory _factory = factory;

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
            _factory.AddEntry(new TestLogEntry(logLevel, formatter(state, exception), exception));
    }

    public sealed record TestLogEntry(LogLevel LogLevel, string Message, Exception? Exception);
}
