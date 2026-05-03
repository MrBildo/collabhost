using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;

using Collabhost.Api.Proxy;
using Collabhost.Api.Tests.Fixtures;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Platform;

// Wire-contract tests for the additive fields landed by card #217:
// - portalReachable (bool) is a pure function of proxyState
// - proxyDetail (object | null) appears for Degraded and Failed only
[Collection("Api")]
public class SystemStatusDegradedShapeTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture;

    [Theory]
    [InlineData(ProxyState.Running, true)]
    [InlineData(ProxyState.Starting, false)]
    [InlineData(ProxyState.Degraded, false)]
    [InlineData(ProxyState.Failed, false)]
    [InlineData(ProxyState.Disabled, false)]
    [InlineData(ProxyState.Stopped, false)]
    public async Task GetStatus_PortalReachable_DerivedFromProxyState
    (
        ProxyState state,
        bool expectedReachable
    )
    {
        var proxyManager = _fixture.Services.GetRequiredService<ProxyManager>();
        SetCurrentState(proxyManager, state);

        try
        {
            using var response = await _fixture.Client.GetAsync(new Uri("/api/v1/status", UriKind.Relative));

            response.IsSuccessStatusCode.ShouldBeTrue();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            json.TryGetProperty("portalReachable", out var portalReachable).ShouldBeTrue("portalReachable field must be present");
            portalReachable.ValueKind.ShouldBe(expectedReachable ? JsonValueKind.True : JsonValueKind.False);
        }
        finally
        {
            SetCurrentState(proxyManager, ProxyState.Starting);
        }
    }

    [Theory]
    [InlineData(ProxyState.Running)]
    [InlineData(ProxyState.Starting)]
    [InlineData(ProxyState.Disabled)]
    [InlineData(ProxyState.Stopped)]
    public async Task GetStatus_ProxyDetail_NullForHealthyOrPassiveStates(ProxyState state)
    {
        var proxyManager = _fixture.Services.GetRequiredService<ProxyManager>();
        SetCurrentState(proxyManager, state);

        try
        {
            using var response = await _fixture.Client.GetAsync(new Uri("/api/v1/status", UriKind.Relative));

            response.IsSuccessStatusCode.ShouldBeTrue();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            json.TryGetProperty("proxyDetail", out var proxyDetail).ShouldBeTrue("proxyDetail field must be present");
            proxyDetail.ValueKind.ShouldBe(JsonValueKind.Null);
        }
        finally
        {
            SetCurrentState(proxyManager, ProxyState.Starting);
        }
    }

    [Theory]
    [InlineData(ProxyState.Degraded)]
    [InlineData(ProxyState.Failed)]
    public async Task GetStatus_ProxyDetail_PopulatedForDegradedAndFailed(ProxyState state)
    {
        var proxyManager = _fixture.Services.GetRequiredService<ProxyManager>();
        SetCurrentState(proxyManager, state);

        try
        {
            using var response = await _fixture.Client.GetAsync(new Uri("/api/v1/status", UriKind.Relative));

            response.IsSuccessStatusCode.ShouldBeTrue();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            json.TryGetProperty("proxyDetail", out var proxyDetail).ShouldBeTrue("proxyDetail field must be present");
            proxyDetail.ValueKind.ShouldBe(JsonValueKind.Object);

            // Shape contract: required keys must be present even when no sync has run yet.
            proxyDetail.TryGetProperty("lastSyncOk", out var lastSyncOk).ShouldBeTrue("lastSyncOk required");
            lastSyncOk.ValueKind.ShouldBe(JsonValueKind.False);

            proxyDetail.TryGetProperty("listenAddress", out var listenAddress).ShouldBeTrue("listenAddress required");
            listenAddress.ValueKind.ShouldBe(JsonValueKind.String);
            listenAddress.GetString().ShouldNotBeNullOrWhiteSpace();
        }
        finally
        {
            SetCurrentState(proxyManager, ProxyState.Starting);
        }
    }

    private static void SetCurrentState(ProxyManager manager, ProxyState state)
    {
        var field = typeof(ProxyManager).GetField
        (
            "_currentState",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        field.ShouldNotBeNull();
        field.SetValue(manager, (int)state);
    }
}
