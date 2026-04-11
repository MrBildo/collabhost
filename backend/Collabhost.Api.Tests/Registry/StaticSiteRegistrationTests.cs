using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Registry;

[Collection("Api")]
public class StaticSiteRegistrationTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task RegisterStaticSite_ShowsStoppedOnList()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"test-static-{suffix}";

        try
        {
            // Register a static-site app
            var createPayload = new
            {
                name = slug,
                displayName = "Test Static Site",
                appTypeSlug = "static-site"
            };

            using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
            createRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
            createRequest.Content = JsonContent.Create(createPayload, options: _jsonOptions);

            var createResponse = await _client.SendAsync(createRequest);

            createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

            // Fetch the app list and find our app
            using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/apps");
            listRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var listResponse = await _client.SendAsync(listRequest);

            listResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await listResponse.Content.ReadAsStringAsync();
            var items = JsonDocument.Parse(body).RootElement;

            var app = FindAppBySlug(items, slug);

            app.ShouldNotBeNull("Static-site app should appear in the list after registration");
            app.Value.GetProperty("status").GetString().ShouldBe("stopped");
        }
        finally
        {
            using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/apps/{slug}");
            deleteRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            await _client.SendAsync(deleteRequest);
        }
    }

    [Fact]
    public async Task RegisterStaticSite_RouteDisabledOnList()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"test-static-{suffix}";

        try
        {
            var createPayload = new
            {
                name = slug,
                displayName = "Test Static Route",
                appTypeSlug = "static-site"
            };

            using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
            createRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
            createRequest.Content = JsonContent.Create(createPayload, options: _jsonOptions);

            var createResponse = await _client.SendAsync(createRequest);

            createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

            // Fetch the app list and verify route is disabled
            using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/apps");
            listRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var listResponse = await _client.SendAsync(listRequest);

            listResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await listResponse.Content.ReadAsStringAsync();
            var items = JsonDocument.Parse(body).RootElement;

            var app = FindAppBySlug(items, slug);

            app.ShouldNotBeNull("Static-site app should appear in the list after registration");
            app.Value.GetProperty("domainActive").GetBoolean().ShouldBeFalse
            (
                "Newly registered static site should have route disabled until explicitly started"
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
    public async Task RegisterStaticSite_DetailShowsStoppedAndRouteDisabled()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"test-static-{suffix}";

        try
        {
            var createPayload = new
            {
                name = slug,
                displayName = "Test Static Detail",
                appTypeSlug = "static-site"
            };

            using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
            createRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
            createRequest.Content = JsonContent.Create(createPayload, options: _jsonOptions);

            var createResponse = await _client.SendAsync(createRequest);

            createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

            // Fetch detail and verify both status and route
            using var detailRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{slug}");
            detailRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var detailResponse = await _client.SendAsync(detailRequest);

            detailResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await detailResponse.Content.ReadAsStringAsync();
            var detail = JsonDocument.Parse(body).RootElement;

            detail.GetProperty("status").GetString().ShouldBe("stopped");
            detail.GetProperty("domainActive").GetBoolean().ShouldBeFalse
            (
                "Newly registered static site should have route disabled on detail endpoint"
            );
        }
        finally
        {
            using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/apps/{slug}");
            deleteRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            await _client.SendAsync(deleteRequest);
        }
    }

    private static JsonElement? FindAppBySlug(JsonElement array, string slug)
    {
        foreach (var item in array.EnumerateArray())
        {
            if (string.Equals(item.GetProperty("name").GetString(), slug, StringComparison.Ordinal))
            {
                return item;
            }
        }

        return null;
    }
}
