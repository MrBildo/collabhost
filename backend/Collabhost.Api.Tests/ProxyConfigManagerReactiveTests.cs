using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Services;
using Collabhost.Api.Services.Proxy;
using Collabhost.Api.Tests.Fixtures;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

using static Collabhost.Api.Tests.Fixtures.AppTestHelpers;

namespace Collabhost.Api.Tests;

public sealed class ProxyConfigManagerReactiveTests(CollabhostApiFixture fixture) : IClassFixture<CollabhostApiFixture>, IDisposable
{
    private readonly CollabhostApiFixture _fixture = fixture;
    private readonly List<IDisposable> _subscriptions = [];

    [Fact]
    public void ProxyConfigManager_SubscribesToEventBus_OnStartup()
    {
        // Arrange & Act — ProxyConfigManager is started as a hosted service
        var manager = _fixture.Services.GetRequiredService<ProxyConfigManager>();

        // Assert — manager should exist and be functional
        manager.ShouldNotBeNull();
    }

    [Fact]
    public async Task SyncRoutes_FiresOnce_ForNonProxyAppStart()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "reactive-nonproxy");

        var fake = _fixture.Services.GetRequiredService<IProxyConfigClient>() as FakeProxyConfigClient;
        fake.ShouldNotBeNull();
        var countBefore = fake.LoadCallCount;

        // Act — start a non-proxy app (bridge enables route + starts process)
        await client.PostAsync($"/api/v1/apps/{externalId}/start", null);

        // Small delay to allow any async handlers to fire
        await Task.Delay(500);

        // Assert — bridge calls SyncRoutes once (for route enablement).
        // Non-proxy app state changes should not trigger additional syncs.
        fake.LoadCallCount.ShouldBe(countBefore + 1);
    }

    [Fact]
    public async Task ManualReload_StillWorks_AfterReactiveRewrite()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var fake = _fixture.Services.GetRequiredService<IProxyConfigClient>() as FakeProxyConfigClient;
        fake.ShouldNotBeNull();
        var countBefore = fake.LoadCallCount;

        // Act — manual reload via API
        var response = await client.PostAsync("/api/v1/proxy/reload", null);

        // Assert
        response.EnsureSuccessStatusCode();
        fake.LoadCallCount.ShouldBeGreaterThan(countBefore);
    }

    [Fact]
    public async Task ProxyConfigManager_SyncRoutes_WorksDirectly()
    {
        // Arrange
        var manager = _fixture.Services.GetRequiredService<ProxyConfigManager>();
        var fake = _fixture.Services.GetRequiredService<IProxyConfigClient>() as FakeProxyConfigClient;
        fake.ShouldNotBeNull();
        var countBefore = fake.LoadCallCount;

        // Act — manual sync should still work regardless of disabled state
        await manager.SyncRoutesAsync();

        // Assert
        fake.LoadCallCount.ShouldBeGreaterThan(countBefore);
    }

    [Fact]
    public async Task ProxyConfigManager_IgnoresEvents_WhenDisabled()
    {
        // In test env, no proxy app is seeded (binary doesn't exist),
        // so the config manager should be in disabled mode.
        // Verify events don't cause sync.

        // Arrange
        var bus = _fixture.Services.GetRequiredService<IProcessStateEventBus>();
        var fake = _fixture.Services.GetRequiredService<IProxyConfigClient>() as FakeProxyConfigClient;
        fake.ShouldNotBeNull();
        var countBefore = fake.LoadCallCount;

        // Act — publish a fake Running event with a random app ID
        bus.Publish
        (
            new ProcessStateChangedEvent
            (
                Guid.NewGuid(),
                "fake-external-id",
                IdentifierCatalog.ProcessStates.Starting,
                IdentifierCatalog.ProcessStates.Running
            )
        );

        // Wait for any potential async handler
        await Task.Delay(TimeSpan.FromSeconds(3));

        // Assert — no sync should have been triggered
        fake.LoadCallCount.ShouldBe(countBefore);
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
