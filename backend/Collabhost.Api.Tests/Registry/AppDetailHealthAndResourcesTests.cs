using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Registry;

// Cluster B / card #221 -- the supervisor's resource sampler and the health-check
// executor produce values that previously were always-null on AppDetail. These tests
// pin the contract for the not-running case (the only state we can reach in an
// integration test without spawning real processes inside the fixture):
//
//   - The fields exist on the response (JSON contract pinned)
//   - When the app is registered but not running, both fields are JSON null
//
// The "running app -> healthStatus reflects probe outcome" path needs a real spawned
// process and is exercised by manual UAT and by the per-component unit tests under
// Tests/HealthChecks and Tests/Supervisor/Resources.
[Collection("Api")]
public class AppDetailHealthAndResourcesTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task GetAppDetail_NotRunning_HealthStatusAndResourcesAreNull()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"health-resources-{suffix}";

        var createPayload = new
        {
            name = slug,
            displayName = "Health + Resources Wiring Test",
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

        // The card #221 audit flagged healthStatus and resources as always-null in
        // the AppDetail response. Pin the property names here so a future rename
        // would surface as a test failure rather than a silent contract break.
        doc.AsObject().ContainsKey("healthStatus").ShouldBeTrue();
        doc.AsObject().ContainsKey("resources").ShouldBeTrue();

        doc["healthStatus"]?.GetValue<string?>().ShouldBeNull();
        doc["resources"].ShouldBeNull();

        // Cleanup
        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/apps/{slug}");
        deleteRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        await _client.SendAsync(deleteRequest);
    }
}
