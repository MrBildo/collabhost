using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Proxy;

// Card #361 end-to-end coverage for the internal-service app type.
//
// internal-service is the inverse of external-route (#348): Collabhost runs
// and supervises the process, but no Caddy route is generated. Targets non-
// HTTP managed services (databases, key-value stores like Valkey, message
// brokers, custom-protocol upstreams) where an auto-generated reverse_proxy
// route would be a permanent 502 (no TCP listener, or a non-HTTP listener
// that Caddy cannot speak).
//
// Coverage:
//   - Register an internal-service app via POST /api/v1/apps -> created,
//     no auto-route, no domain.
//   - The created app appears in /api/v1/apps with status="stopped" and
//     domainActive=false / domain absent.
//   - The proxy /api/v1/routes listing does NOT contain the new app's slug.
//   - The AppDetail response carries no `route` field (route is null when
//     routingConfiguration is null).
//
// Negative-guard for regression: the existing executable type STILL emits a
// route. Covered by ExternalRouteIntegrationTests + ProxyConfigurationBuilder
// tests already; this file's discipline is "non-routing process app stays
// non-routing."
[Collection("Api")]
public class InternalServiceRoutingTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task Register_InternalService_DoesNotGenerateRoute()
    {
        var slug = NewSlug();

        try
        {
            await CreateAsync(slug);

            using var routesRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/routes");
            routesRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var routesResponse = await _client.SendAsync(routesRequest);

            routesResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = JsonDocument.Parse(await routesResponse.Content.ReadAsStringAsync()).RootElement;
            var routes = body.GetProperty("routes");

            foreach (var route in routes.EnumerateArray())
            {
                var routeSlug = route.GetProperty("appName").GetString();

                routeSlug.ShouldNotBe
                (
                    slug,
                    "internal-service apps must not produce a Caddy route -- the type is for "
                    + "non-HTTP managed processes where a reverse_proxy route is meaningless."
                );
            }
        }
        finally
        {
            await DeleteAsync(slug);
        }
    }

    [Fact]
    public async Task Register_InternalService_DetailReportsNoRoute()
    {
        var slug = NewSlug();

        try
        {
            await CreateAsync(slug);

            using var detailRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{slug}");
            detailRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var detailResponse = await _client.SendAsync(detailRequest);

            detailResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            var detail = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync()).RootElement;

            // The route field is serialized as null when no routing capability is
            // bound. AppDetail.route is `AppRoute?` -- a null value emits a
            // `"route": null` JSON property.
            if (detail.TryGetProperty("route", out var route))
            {
                route.ValueKind.ShouldBe
                (
                    JsonValueKind.Null,
                    "internal-service apps should expose no route information -- "
                    + "the type omits the routing capability binding."
                );
            }

            // domainActive should be false (no route -> nothing to activate).
            detail.GetProperty("domainActive").GetBoolean().ShouldBeFalse();
        }
        finally
        {
            await DeleteAsync(slug);
        }
    }

    [Fact]
    public async Task Register_InternalService_AppearsInList()
    {
        var slug = NewSlug();

        try
        {
            await CreateAsync(slug);

            using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/apps");
            listRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var list = JsonDocument.Parse(await (await _client.SendAsync(listRequest)).Content.ReadAsStringAsync()).RootElement;

            var found = false;

            foreach (var item in list.EnumerateArray())
            {
                if (string.Equals(item.GetProperty("name").GetString(), slug, StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }
            }

            found.ShouldBeTrue
            (
                "internal-service apps should be operator-visible in the apps list -- the "
                + "type is NOT marked isInternal (which would hide it from the picker)."
            );
        }
        finally
        {
            await DeleteAsync(slug);
        }
    }

    // ---- helpers ----

    private static string NewSlug() => $"int-{Guid.NewGuid().ToString("N")[..8]}";

    private async Task CreateAsync(string slug)
    {
        var payload = new
        {
            name = slug,
            displayName = "Internal Service Test",
            appTypeSlug = "internal-service",
            values = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal)
            {
                ["process"] = new(StringComparer.Ordinal)
                {
                    ["command"] = "/usr/bin/test",
                    ["workingDirectory"] = "/tmp"
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
