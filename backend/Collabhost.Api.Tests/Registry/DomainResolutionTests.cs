using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Registry;

// Regression tests for Card #144 -- verify that domain display uses the configured base domain,
// not the hardcoded "collab.internal" default. ApiFixture sets BaseDomain = "test.internal".
[Collection("Api")]
public class DomainResolutionTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task GetApp_Domain_ReflectsConfiguredBaseDomain()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"domain-test-{suffix}";

        var createPayload = new
        {
            name = slug,
            displayName = "Domain Resolution Test",
            appTypeSlug = "dotnet-app",
            values = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["artifact"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["location"] = "/tmp/dummy"
                }
            }
        };

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
        createRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        createRequest.Content = JsonContent.Create(createPayload, options: _jsonOptions);

        var createResponse = await _client.SendAsync(createRequest);

        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{slug}");
        getRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var getResponse = await _client.SendAsync(getRequest);

        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await getResponse.Content.ReadAsStringAsync();
        var doc = JsonNode.Parse(body)!;

        var domain = doc["domain"]?.GetValue<string>();

        domain.ShouldNotBeNull();
        domain.ShouldBe($"{slug}.test.internal");
        domain.ShouldNotContain("collab.internal");

        // Cleanup
        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/apps/{slug}");
        deleteRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        await _client.SendAsync(deleteRequest);
    }
}
