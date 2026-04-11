using System.Net;
using System.Text;
using System.Text.Json;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Probes;

[Collection("Api")]
public class ProbeEndpointTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    [Fact]
    public async Task AppDetail_ReturnsEmptyProbesArray_WhenNeverStarted()
    {
        // Create a test app
        var appSlug = $"probe-test-{Guid.NewGuid():N}"[..20];

        var createBody = JsonSerializer.Serialize(new
        {
            name = appSlug,
            displayName = "Probe Test",
            appTypeSlug = "dotnet-app"
        });

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps")
        {
            Content = new StringContent(createBody, Encoding.UTF8, "application/json")
        };
        createRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var createResponse = await _client.SendAsync(createRequest);

        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Get detail
        using var detailRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{appSlug}");
        detailRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var detailResponse = await _client.SendAsync(detailRequest);

        detailResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await detailResponse.Content.ReadAsStringAsync();
        var detail = JsonDocument.Parse(body).RootElement;

        detail.TryGetProperty("probes", out var probes).ShouldBeTrue("AppDetail should have a probes field");
        probes.ValueKind.ShouldBe(JsonValueKind.Array);
        probes.GetArrayLength().ShouldBe(0);

        // Clean up
        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/apps/{appSlug}");
        deleteRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        await _client.SendAsync(deleteRequest);
    }

    [Fact]
    public async Task AppDetail_DoesNotHaveTagsField()
    {
        // Create a test app
        var appSlug = $"notags-test-{Guid.NewGuid():N}"[..20];

        var createBody = JsonSerializer.Serialize(new
        {
            name = appSlug,
            displayName = "No Tags Test",
            appTypeSlug = "dotnet-app"
        });

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps")
        {
            Content = new StringContent(createBody, Encoding.UTF8, "application/json")
        };
        createRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        await _client.SendAsync(createRequest);

        // Get detail
        using var detailRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{appSlug}");
        detailRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var detailResponse = await _client.SendAsync(detailRequest);
        var body = await detailResponse.Content.ReadAsStringAsync();
        var detail = JsonDocument.Parse(body).RootElement;

        // Tags field should NOT be present (replaced by probes)
        detail.TryGetProperty("tags", out _).ShouldBeFalse("AppDetail should not have a tags field -- replaced by probes");

        // Clean up
        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/apps/{appSlug}");
        deleteRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        await _client.SendAsync(deleteRequest);
    }
}
