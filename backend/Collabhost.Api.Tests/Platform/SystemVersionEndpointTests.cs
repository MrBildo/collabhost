using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Platform;

// Integration tests for GET /api/v1/version. Locks the wire shape that the UAT runbook
// and the operator-side `curl /api/v1/version | jq .wwwrootHash` flow assert against.
// Card #342.
[Collection("Api")]
public class SystemVersionEndpointTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture;

    [Fact]
    public async Task GetVersion_CarriesAllFourFields()
    {
        using var response = await _fixture.Client.GetAsync
        (
            new Uri("/api/v1/version", UriKind.Relative)
        );

        response.IsSuccessStatusCode.ShouldBeTrue();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.TryGetProperty("version", out _).ShouldBeTrue("version field must be present");
        json.TryGetProperty("commit", out _).ShouldBeTrue("commit field must be present");
        json.TryGetProperty("platform", out _).ShouldBeTrue("platform field must be present");
        json.TryGetProperty("wwwrootHash", out _).ShouldBeTrue("wwwrootHash field must be present (#342)");
    }

    [Fact]
    public async Task GetVersion_WwwrootHashIsString()
    {
        using var response = await _fixture.Client.GetAsync
        (
            new Uri("/api/v1/version", UriKind.Relative)
        );

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        var hash = json.GetProperty("wwwrootHash");

        // Always serialized as a string -- empty string for dev/test builds (no
        // AssemblyMetadataAttribute), 64-hex digest for archive-published builds.
        // Never null, never absent, never a number.
        hash.ValueKind.ShouldBe(JsonValueKind.String);

        var value = hash.GetString();
        value.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetVersion_IsPublicAndRequiresNoAuth()
    {
        // The UAT runbook §0 pre-flight calls /api/v1/version before the operator
        // pastes the admin key. Confirm the endpoint stays public (no X-User-Key).
        using var client = _fixture.CreateClient();
        using var response = await client.GetAsync(new Uri("/api/v1/version", UriKind.Relative));

        response.IsSuccessStatusCode.ShouldBeTrue();
    }
}
