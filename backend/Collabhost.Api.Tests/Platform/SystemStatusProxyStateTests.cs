using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;

using Collabhost.Api.Proxy;
using Collabhost.Api.Tests.Fixtures;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Platform;

// Integration-ish tests for the proxyState field on GET /api/v1/status.
// All 5 enum values must be representable in the wire shape (lowercase).
// Transitions are forced via reflection on the private _currentState field --
// contract-boundary test, not a behavior test.
[Collection("Api")]
public class SystemStatusProxyStateTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture;

    [Fact]
    public async Task GetStatus_ResponseIncludesProxyStateField()
    {
        using var response = await _fixture.Client.GetAsync(new Uri("/api/v1/status", UriKind.Relative));

        response.IsSuccessStatusCode.ShouldBeTrue();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.TryGetProperty("proxyState", out var proxyState).ShouldBeTrue("proxyState field must be present");
        proxyState.ValueKind.ShouldBe(JsonValueKind.String);
    }

    [Theory]
    [InlineData(ProxyState.Starting, "starting")]
    [InlineData(ProxyState.Running, "running")]
    [InlineData(ProxyState.Failed, "failed")]
    [InlineData(ProxyState.Disabled, "disabled")]
    [InlineData(ProxyState.Stopped, "stopped")]
    public async Task GetStatus_AllStates_SurfaceAsLowercase(ProxyState state, string expectedWire)
    {
        var proxyManager = _fixture.Services.GetRequiredService<ProxyManager>();

        SetCurrentState(proxyManager, state);

        try
        {
            using var response = await _fixture.Client.GetAsync(new Uri("/api/v1/status", UriKind.Relative));

            response.IsSuccessStatusCode.ShouldBeTrue();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            json.GetProperty("proxyState").GetString().ShouldBe(expectedWire);
        }
        finally
        {
            // Leave the fixture in a stable state for sibling tests.
            SetCurrentState(proxyManager, ProxyState.Starting);
        }
    }

    // Reflection access to the volatile _currentState field. Load-bearing: we're
    // asserting the wire contract, not exercising transition behavior.
    private static void SetCurrentState(ProxyManager manager, ProxyState state)
    {
        var field = typeof(ProxyManager).GetField
        (
            "_currentState",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        field.ShouldNotBeNull();

        field.SetValue(manager, state);
    }
}
