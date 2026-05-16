using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Registry;

// Covers #326 / #322 decision E1 -- the per-app writable data path is a
// first-class API contract. Pins:
//   - the registration response carries `writableDataPath`
//   - get_app carries `writableDataPath`
//   - both agree for the same app (one resolver, one shape)
//   - the value is an absolute path ending in app-data/<slug>
//
// Path-separator-agnostic and fixture-temp-dir-agnostic on purpose: the test
// pins the contract (field present, absolute, slug-anchored, stable across
// surfaces), not the fixture's transient data root.
[Collection("Api")]
public class WritableDataPathContractTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task RegistrationAndGetApp_BothCarryWritableDataPath_AndAgree()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"writable-path-{suffix}";

        var createPayload = new
        {
            name = slug,
            displayName = "Writable Data Path Contract",
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

        var createBody = await createResponse.Content.ReadAsStringAsync();
        var createDoc = JsonNode.Parse(createBody)!.AsObject();

        createDoc.ContainsKey("writableDataPath").ShouldBeTrue();

        var registrationPath = createDoc["writableDataPath"]!.GetValue<string>();

        registrationPath.ShouldNotBeNullOrWhiteSpace();
        Path.IsPathRooted(registrationPath).ShouldBeTrue();

        // app-data/<slug> leaf, separator-agnostic.
        var expectedLeaf = Path.Combine("app-data", slug);
        registrationPath.ShouldEndWith(expectedLeaf);

        using var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{slug}");
        getRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var getResponse = await _client.SendAsync(getRequest);

        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var getBody = await getResponse.Content.ReadAsStringAsync();
        var getDoc = JsonNode.Parse(getBody)!.AsObject();

        getDoc.ContainsKey("writableDataPath").ShouldBeTrue();

        var detailPath = getDoc["writableDataPath"]!.GetValue<string>();

        // One resolver, one shape: registration and get_app agree for the same app.
        detailPath.ShouldBe(registrationPath);

        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/apps/{slug}");
        deleteRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        await _client.SendAsync(deleteRequest);
    }
}
