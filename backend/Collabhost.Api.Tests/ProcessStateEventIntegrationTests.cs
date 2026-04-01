using System.Net;

using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Services;
using Collabhost.Api.Tests.Fixtures;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

using static Collabhost.Api.Tests.Fixtures.AppTestHelpers;

namespace Collabhost.Api.Tests;

public sealed class ProcessStateEventIntegrationTests(CollabhostApiFixture fixture) : IClassFixture<CollabhostApiFixture>, IDisposable
{
    private readonly CollabhostApiFixture _fixture = fixture;
    private readonly List<IDisposable> _subscriptions = [];

    [Fact]
    public async Task StartApp_PublishesStartingAndRunningEvents()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "event-start");
        var events = new List<ProcessStateChangedEvent>();
        var bus = _fixture.Services.GetRequiredService<IProcessStateEventBus>();
        _subscriptions.Add(bus.Subscribe(e => events.Add(e)));

        // Act
        var response = await client.PostAsync($"/api/v1/apps/{externalId}/start", null);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        events.Count.ShouldBe(2);

        events[0].PreviousStateId.ShouldBe(IdentifierCatalog.ProcessStates.Stopped);
        events[0].NewStateId.ShouldBe(IdentifierCatalog.ProcessStates.Starting);

        events[1].PreviousStateId.ShouldBe(IdentifierCatalog.ProcessStates.Starting);
        events[1].NewStateId.ShouldBe(IdentifierCatalog.ProcessStates.Running);

        events[0].AppExternalId.ShouldBe(externalId);
        events[1].AppExternalId.ShouldBe(externalId);
    }

    [Fact]
    public async Task StopApp_PublishesStoppingAndStoppedEvents()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "event-stop");
        await client.PostAsync($"/api/v1/apps/{externalId}/start", null);

        var events = new List<ProcessStateChangedEvent>();
        var bus = _fixture.Services.GetRequiredService<IProcessStateEventBus>();
        _subscriptions.Add(bus.Subscribe(e => events.Add(e)));

        // Act
        var response = await client.PostAsync($"/api/v1/apps/{externalId}/stop", null);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        events.Count.ShouldBe(2);

        events[0].PreviousStateId.ShouldBe(IdentifierCatalog.ProcessStates.Running);
        events[0].NewStateId.ShouldBe(IdentifierCatalog.ProcessStates.Stopping);

        events[1].PreviousStateId.ShouldBe(IdentifierCatalog.ProcessStates.Stopping);
        events[1].NewStateId.ShouldBe(IdentifierCatalog.ProcessStates.Stopped);
    }

    [Fact]
    public async Task RestartApp_PublishesStopAndStartEvents()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "event-restart");
        await client.PostAsync($"/api/v1/apps/{externalId}/start", null);

        var events = new List<ProcessStateChangedEvent>();
        var bus = _fixture.Services.GetRequiredService<IProcessStateEventBus>();
        _subscriptions.Add(bus.Subscribe(e => events.Add(e)));

        // Act
        var response = await client.PostAsync($"/api/v1/apps/{externalId}/restart", null);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        events.Count.ShouldBe(4);

        // Stop sequence
        events[0].NewStateId.ShouldBe(IdentifierCatalog.ProcessStates.Stopping);
        events[1].NewStateId.ShouldBe(IdentifierCatalog.ProcessStates.Stopped);

        // Start sequence
        events[2].NewStateId.ShouldBe(IdentifierCatalog.ProcessStates.Starting);
        events[3].NewStateId.ShouldBe(IdentifierCatalog.ProcessStates.Running);
    }

    [Fact]
    public async Task StartApp_EventsContainCorrectAppIdentifiers()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "event-ids");
        var events = new List<ProcessStateChangedEvent>();
        var bus = _fixture.Services.GetRequiredService<IProcessStateEventBus>();
        _subscriptions.Add(bus.Subscribe(e => events.Add(e)));

        // Act
        await client.PostAsync($"/api/v1/apps/{externalId}/start", null);

        // Assert
        events.ShouldNotBeEmpty();

        foreach (var processEvent in events)
        {
            processEvent.AppExternalId.ShouldBe(externalId);
            processEvent.AppId.ShouldNotBe(Guid.Empty);
        }

        // All events for the same app should have the same AppId
        events.Select(e => e.AppId).Distinct().Count().ShouldBe(1);
    }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
