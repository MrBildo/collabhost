using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Platform;

// /api/v1/status carries portalUrl so the login screen and dashboard chrome can show
// the proxy-fronted URL after an operator changes Portal:Subdomain. Card #184.
// ApiFixture sets Proxy:BaseDomain = "test.internal"; Portal:Subdomain falls back to
// the hardcoded default "collabhost".
[Collection("Api")]
public class SystemStatusPortalUrlTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture;

    [Fact]
    public async Task GetStatus_ReturnsPortalUrl_WithDefaultPortalSubdomain()
    {
        using var response = await _fixture.Client.GetAsync(new Uri("/api/v1/status", UriKind.Relative));

        response.IsSuccessStatusCode.ShouldBeTrue();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.TryGetProperty("portalUrl", out var portalUrl).ShouldBeTrue();
        portalUrl.GetString().ShouldBe("https://collabhost.test.internal");
    }
}
