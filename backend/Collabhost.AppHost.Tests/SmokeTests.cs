using System.Net;

using Shouldly;

using Xunit;

namespace Collabhost.AppHost.Tests;

[Collection("AppHost")]
public class SmokeTests(AppHostFixture fixture)
{
    private readonly HttpClient _client = fixture.ApiClient;

    [Fact]
    public async Task HealthCheck_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthCheck_ReturnsHealthyBody()
    {
        var response = await _client.GetAsync("/health");

        var body = await response.Content.ReadAsStringAsync();

        body.ShouldBe("Healthy");
    }

    [Fact]
    public async Task AlivenessCheck_ReturnsOk()
    {
        var response = await _client.GetAsync("/alive");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
