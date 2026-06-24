using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Registry;

// Card #438 (FE-UI-05 backend half): AppListItem (/api/v1/apps) now carries a `scheme`
// field ("https" | "http") so the list view can build "{scheme}://{domain}" instead of
// hardcoding "https://". The field is derived from the same proxy listen-surface check
// AppDetail.Route.Tls uses, so the two surfaces agree app-for-app -- which is what these
// pin (rather than hardcoding the fixture's specific listen address).
[Collection("Api")]
public class AppListItemSchemeTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task ListApps_Item_CarriesSchemeMatchingDetailRouteTls()
    {
        var slug = await CreateExternalRouteAppAsync();

        try
        {
            var listScheme = await ReadListSchemeAsync(slug);
            var detailTls = await ReadDetailRouteTlsAsync(slug);

            // The contract: scheme is "https" iff the App Detail route reports TLS, and "http"
            // otherwise -- the same derivation, surfaced on the list item.
            listScheme.ShouldBe(detailTls ? "https" : "http");
        }
        finally
        {
            await DeleteAsync(slug);
        }
    }

    [Fact]
    public async Task ListApps_Item_SchemeIsHttpOrHttps()
    {
        var slug = await CreateExternalRouteAppAsync();

        try
        {
            var listScheme = await ReadListSchemeAsync(slug);

            listScheme.ShouldBeOneOf("https", "http");
        }
        finally
        {
            await DeleteAsync(slug);
        }
    }

    private async Task<string?> ReadListSchemeAsync(string slug)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/apps");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var list = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        foreach (var item in list.EnumerateArray())
        {
            if (item.GetProperty("name").GetString() == slug)
            {
                // The field must be present (not just absent-and-defaulting).
                item.TryGetProperty("scheme", out var scheme).ShouldBeTrue
                (
                    "AppListItem must carry a 'scheme' field (Card #438)."
                );

                return scheme.GetString();
            }
        }

        throw new InvalidOperationException($"App '{slug}' not found in /api/v1/apps response.");
    }

    private async Task<bool> ReadDetailRouteTlsAsync(string slug)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{slug}");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var detail = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        return detail.GetProperty("route").GetProperty("tls").GetBoolean();
    }

    private async Task<string> CreateExternalRouteAppAsync()
    {
        var slug = $"scheme-ext-{Guid.NewGuid().ToString("N")[..8]}";

        var payload = new
        {
            name = slug,
            displayName = "Scheme Test External Route",
            appTypeSlug = "external-route",
            values = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal)
            {
                ["external-target"] = new(StringComparer.Ordinal)
                {
                    ["host"] = "192.168.1.50",
                    ["port"] = 11235,
                    ["scheme"] = "http"
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        request.Content = JsonContent.Create(payload, options: _jsonOptions);

        var response = await _client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return slug;
    }

    private async Task DeleteAsync(string slug)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/apps/{slug}");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        await _client.SendAsync(request);
    }
}
