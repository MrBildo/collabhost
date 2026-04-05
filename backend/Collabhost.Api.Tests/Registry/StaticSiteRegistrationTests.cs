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
                appTypeId = "01KN8K1MRT26VCX65J1ZSVWESB"
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
