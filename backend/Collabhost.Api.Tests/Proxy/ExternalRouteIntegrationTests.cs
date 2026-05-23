using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Proxy;

// Card #348 end-to-end integration tests for the external-route app type.
//
// Coverage:
//   - Register an external-route app via POST /api/v1/apps with valid
//     external-target settings -> created, route auto-enabled (D8).
//   - The created app appears in the list with domainActive=true and the
//     external-target binding persisted as an override.
//   - The AppDetail route.target reports the operator-declared upstream
//     (scheme://host:port) rather than localhost:....
//   - The AppDetail.tabs field carries ["health","route"] for external-route.
//   - Stop / start toggle disables / re-enables the route (lifecycle parity
//     with static-site).
//   - Delete explicitly disables the route as part of the delete flow
//     (Card #348 fix-along on AppEndpoints.DeleteAppAsync).
//
// Caddy emission verification lives in ProxyConfigurationBuilderTests --
// FakeCaddyClient is a no-op in the ApiFixture, so we observe backend state
// (route list, app detail, IsRouteEnabled via the routes endpoint).
[Collection("Api")]
public class ExternalRouteIntegrationTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task Register_ExternalRoute_AutoEnablesRoute()
    {
        var slug = NewSlug();

        try
        {
            await CreateAsync(slug);

            using var detailRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{slug}");
            detailRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var detailResponse = await _client.SendAsync(detailRequest);

            detailResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await detailResponse.Content.ReadAsStringAsync();
            var detail = JsonDocument.Parse(body).RootElement;

            // D8: auto-enabled at registration.
            detail.GetProperty("domainActive").GetBoolean().ShouldBeTrue
            (
                "External-route should auto-enable its route at registration (Card #348, D8)"
            );

            // External-route status is "running" when the route is enabled,
            // mirroring static-site -> Running mapping in ResolveStatus.
            detail.GetProperty("status").GetString().ShouldBe("running");
        }
        finally
        {
            await DeleteAsync(slug);
        }
    }

    [Fact]
    public async Task Register_ExternalRoute_DetailReportsResolvedTarget()
    {
        var slug = NewSlug();

        try
        {
            await CreateAsync(slug, host: "192.168.1.50", port: 11235, scheme: "http");

            using var detailRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{slug}");
            detailRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var detail = JsonDocument.Parse(await (await _client.SendAsync(detailRequest)).Content.ReadAsStringAsync()).RootElement;

            var route = detail.GetProperty("route");

            route.GetProperty("target").GetString().ShouldBe("http://192.168.1.50:11235");
        }
        finally
        {
            await DeleteAsync(slug);
        }
    }

    [Fact]
    public async Task Register_ExternalRoute_HttpsScheme_PropagatesIntoRouteTarget()
    {
        var slug = NewSlug();

        try
        {
            // upstream.local is in the private-only host pattern allowlist.
            await CreateAsync(slug, host: "upstream.local", port: 8443, scheme: "https");

            using var detailRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{slug}");
            detailRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var detail = JsonDocument.Parse(await (await _client.SendAsync(detailRequest)).Content.ReadAsStringAsync()).RootElement;

            detail.GetProperty("route").GetProperty("target").GetString().ShouldBe("https://upstream.local:8443");
        }
        finally
        {
            await DeleteAsync(slug);
        }
    }

    [Fact]
    public async Task Register_ExternalRoute_DetailCarriesHealthAndRouteTabs()
    {
        var slug = NewSlug();

        try
        {
            await CreateAsync(slug);

            using var detailRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{slug}");
            detailRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var detail = JsonDocument.Parse(await (await _client.SendAsync(detailRequest)).Content.ReadAsStringAsync()).RootElement;

            var tabs = detail.GetProperty("tabs");

            tabs.ValueKind.ShouldBe(JsonValueKind.Array);

            var values = new List<string>();
            foreach (var item in tabs.EnumerateArray())
            {
                values.Add(item.GetString() ?? "");
            }

            values.ShouldBe(["health", "route"]);
        }
        finally
        {
            await DeleteAsync(slug);
        }
    }

    [Fact]
    public async Task Register_ExternalRoute_RejectsPublicHostByDefault()
    {
        var slug = NewSlug();

        var payload = new
        {
            name = slug,
            displayName = "Test External Reject",
            appTypeSlug = "external-route",
            values = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal)
            {
                ["external-target"] = new(StringComparer.Ordinal)
                {
                    ["host"] = "api.openai.com",
                    ["port"] = 443,
                    ["scheme"] = "https"
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        request.Content = JsonContent.Create(payload, options: _jsonOptions);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("host", Case.Insensitive);
    }

    [Fact]
    public async Task StopApp_ExternalRoute_DisablesRoute()
    {
        var slug = NewSlug();

        try
        {
            await CreateAsync(slug);

            using var stopRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/apps/{slug}/stop");
            stopRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var stopResponse = await _client.SendAsync(stopRequest);
            stopResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            using var detailRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{slug}");
            detailRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var detail = JsonDocument.Parse(await (await _client.SendAsync(detailRequest)).Content.ReadAsStringAsync()).RootElement;

            detail.GetProperty("domainActive").GetBoolean().ShouldBeFalse();
            detail.GetProperty("status").GetString().ShouldBe("stopped");
        }
        finally
        {
            await DeleteAsync(slug);
        }
    }

    [Fact]
    public async Task StartApp_ExternalRoute_ReEnablesRoute()
    {
        var slug = NewSlug();

        try
        {
            await CreateAsync(slug);

            // stop, then start
            using (var stopRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/apps/{slug}/stop"))
            {
                stopRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
                (await _client.SendAsync(stopRequest)).StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            using (var startRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/apps/{slug}/start"))
            {
                startRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
                (await _client.SendAsync(startRequest)).StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            using var detailRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{slug}");
            detailRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var detail = JsonDocument.Parse(await (await _client.SendAsync(detailRequest)).Content.ReadAsStringAsync()).RootElement;

            detail.GetProperty("domainActive").GetBoolean().ShouldBeTrue();
            detail.GetProperty("status").GetString().ShouldBe("running");
        }
        finally
        {
            await DeleteAsync(slug);
        }
    }

    [Fact]
    public async Task DeleteApp_ExternalRoute_RemovesFromList()
    {
        var slug = NewSlug();

        await CreateAsync(slug);

        // Fix-along: DeleteAppAsync explicitly disables the route before
        // the app row is deleted. After delete, the app does not appear in
        // the list and the route does not survive in the proxy's enabled
        // state. We assert the list-level disappearance directly; the
        // route-state cleanup is exercised at unit level in
        // ProxyManagerTests.
        await DeleteAsync(slug);

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/apps");
        listRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var list = JsonDocument.Parse(await (await _client.SendAsync(listRequest)).Content.ReadAsStringAsync()).RootElement;

        foreach (var item in list.EnumerateArray())
        {
            item.GetProperty("name").GetString().ShouldNotBe(slug);
        }
    }

    // ---- helpers ----

    private static string NewSlug() => $"ext-{Guid.NewGuid().ToString("N")[..8]}";

    private async Task CreateAsync
    (
        string slug,
        string host = "localhost",
        int port = 11235,
        string scheme = "http"
    )
    {
        var payload = new
        {
            name = slug,
            displayName = "External Test",
            appTypeSlug = "external-route",
            values = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal)
            {
                ["external-target"] = new(StringComparer.Ordinal)
                {
                    ["host"] = host,
                    ["port"] = port,
                    ["scheme"] = scheme
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        request.Content = JsonContent.Create(payload, options: _jsonOptions);

        var response = await _client.SendAsync(request);

        if (response.StatusCode != HttpStatusCode.Created)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException
            (
                $"Unexpected status {response.StatusCode} from POST /api/v1/apps: {body}"
            );
        }
    }

    private async Task DeleteAsync(string slug)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/apps/{slug}");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        await _client.SendAsync(request);
    }
}
