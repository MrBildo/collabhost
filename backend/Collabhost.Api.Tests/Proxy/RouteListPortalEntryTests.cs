using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Proxy;

// /api/v1/routes returns a synthetic Portal row (IsPortal: true) at index 0 so the
// operator can see the resolved Portal hostname after configuring Portal:Subdomain.
// Card #184. ApiFixture sets Proxy:BaseDomain = "test.internal" and Hosting:ListenPort
// = 58400; the Portal subdomain falls back to the hardcoded default "collabhost".
[Collection("Api")]
public class RouteListPortalEntryTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    [Fact]
    public async Task ListRoutes_IncludesPortalRow_WithDefaultSubdomain()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/routes");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var routes = body.GetProperty("routes");

        routes.GetArrayLength().ShouldBeGreaterThan(0);

        var portalRow = routes[0];
        portalRow.GetProperty("isPortal").GetBoolean().ShouldBeTrue();
        portalRow.GetProperty("appName").GetString().ShouldBe("collabhost");
        portalRow.GetProperty("appDisplayName").GetString().ShouldBe("Collabhost Portal");
        portalRow.GetProperty("domain").GetString().ShouldBe("collabhost.test.internal");
        portalRow.GetProperty("target").GetString().ShouldBe("localhost:58400");
        portalRow.GetProperty("proxyMode").GetString().ShouldBe("reverseProxy");
        portalRow.GetProperty("https").GetBoolean().ShouldBeTrue();
        portalRow.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        portalRow.GetProperty("appExternalId").GetString().ShouldBe(string.Empty);
    }

    [Fact]
    public async Task ListRoutes_AppRowsFollowPortalRow_AndAreNotFlaggedAsPortal()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"portal-row-test-{suffix}";

        try
        {
            var createPayload = new
            {
                name = slug,
                displayName = "Portal Row Sibling",
                appTypeSlug = "static-site"
            };

            using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
            createRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
            createRequest.Content = JsonContent.Create
            (
                createPayload,
                options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
            );

            var createResponse = await _client.SendAsync(createRequest);
            createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

            using var routesRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/routes");
            routesRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var response = await _client.SendAsync(routesRequest);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            var routes = body.GetProperty("routes");

            // Index 0 is always the Portal row.
            routes[0].GetProperty("isPortal").GetBoolean().ShouldBeTrue();

            // Subsequent rows are app routes; none are Portal.
            for (var i = 1; i < routes.GetArrayLength(); i++)
            {
                routes[i].GetProperty("isPortal").GetBoolean().ShouldBeFalse();
            }
        }
        finally
        {
            using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/apps/{slug}");
            deleteRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
            await _client.SendAsync(deleteRequest);
        }
    }
}
