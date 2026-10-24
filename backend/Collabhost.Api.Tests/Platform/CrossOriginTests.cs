using Collabhost.Api.Tests.Fixtures;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Platform;

// A Development-mode API grants no cross-origin access. The dashboard's dev server proxies /api, so
// the browser never calls the API cross-origin; a permissive policy would only let any website a
// developer happens to visit drive the local API with whatever key that developer configured.
[Collection("Api")]
public class CrossOriginTests(ApiFixture fixture)
{
    private const string _foreignOrigin = "http://evil.example";

    private readonly HttpClient _client = fixture.Client;

    // The precondition the two tests below depend on: the fixture boots in Development, the only
    // environment that ever carried a CORS policy. Without it they would pass for the wrong reason.
    [Fact]
    public void Fixture_RunsInDevelopment() =>
        fixture.Services.GetRequiredService<IWebHostEnvironment>().IsDevelopment().ShouldBeTrue();

    [Fact]
    public async Task CrossOriginRequest_IsNotGrantedToAForeignOrigin()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/status");
        request.Headers.Add("Origin", _foreignOrigin);

        using var response = await _client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Fact]
    public async Task Preflight_IsNotGrantedToAForeignOrigin()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/apps");
        request.Headers.Add("Origin", _foreignOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "X-User-Key, Content-Type");

        using var response = await _client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
        response.Headers.Contains("Access-Control-Allow-Headers").ShouldBeFalse();
    }
}
