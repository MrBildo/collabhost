using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Proxy;

/// <summary>
/// Verifies that /api/v1/routes returns operator-friendly display strings in the
/// 'target' field rather than raw Caddy handler names or internal state strings.
/// See card #102.
/// </summary>
[Collection("Api")]
public class RouteListDisplayTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task RouteList_StaticSiteApp_TargetIsStaticFiles()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"test-route-static-{suffix}";

        try
        {
            var createPayload = new
            {
                name = slug,
                displayName = "Route Display Test Static",
                appTypeSlug = "static-site"
            };

            using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
            createRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
            createRequest.Content = JsonContent.Create(createPayload, options: _jsonOptions);

            var createResponse = await _client.SendAsync(createRequest);

            createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

            using var routesRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/routes");
            routesRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var routesResponse = await _client.SendAsync(routesRequest);

            routesResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await routesResponse.Content.ReadAsStringAsync();
            var route = FindRouteByAppName(body, slug);

            route.ShouldNotBeNull("static-site app should appear in the route list");
            route.Value.GetProperty("target").GetString().ShouldBe
            (
                "Static Files",
                "static-site target should be 'Static Files', not the raw Caddy handler name 'file-server'"
            );
        }
        finally
        {
            using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/apps/{slug}");
            deleteRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            await _client.SendAsync(deleteRequest);
        }
    }

    [Fact]
    public async Task RouteList_NotRunningProcessApp_TargetIsNotRunning()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"test-route-proc-{suffix}";

        try
        {
            var createPayload = new
            {
                name = slug,
                displayName = "Route Display Test Process",
                appTypeSlug = "nodejs-app"
            };

            using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
            createRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
            createRequest.Content = JsonContent.Create(createPayload, options: _jsonOptions);

            var createResponse = await _client.SendAsync(createRequest);

            createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

            using var routesRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/routes");
            routesRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var routesResponse = await _client.SendAsync(routesRequest);

            routesResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await routesResponse.Content.ReadAsStringAsync();
            var route = FindRouteByAppName(body, slug);

            route.ShouldNotBeNull("nodejs-app should appear in the route list");
            route.Value.GetProperty("target").GetString().ShouldBe
            (
                "Not Running",
                "a stopped process app's target should be 'Not Running', not the internal string 'not-running'"
            );
        }
        finally
        {
            using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/apps/{slug}");
            deleteRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            await _client.SendAsync(deleteRequest);
        }
    }

    private static JsonElement? FindRouteByAppName(string responseBody, string appName)
    {
        var doc = JsonDocument.Parse(responseBody);

        foreach (var route in doc.RootElement.GetProperty("routes").EnumerateArray())
        {
            if (route.GetProperty("appName").GetString() == appName)
            {
                return route;
            }
        }

        return null;
    }
}
