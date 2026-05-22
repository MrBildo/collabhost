using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Registry;

// Integration coverage for the security-headers (#309) PUT /apps/{slug}/settings
// path. The unit-level cross-field tests in CapabilityResolverTests cover the
// resolver entry points; these tests assert the endpoint correctly invokes
// the post-merge cross-field check (the F1 finding from Kai's PR #213 review).
[Collection("Api")]
public class SecurityHeadersSettingsTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task PutSettings_TwoStep_StsInMapThenEnableHsts_Rejected()
    {
        // The two-step operator-edit path that the in-flight ValidateEdits
        // check alone could NOT catch. Step 1 saves STS into the headers map
        // (passes -- no enableHsts in this delta). Step 2 toggles enableHsts
        // on (no headers in this delta). Neither step trips the in-flight
        // cross-field check. The endpoint MUST run the cross-field check
        // against the post-merge effective override and reject step 2.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"test-309-2step-fwd-{suffix}";

        try
        {
            await CreateAppAsync(slug, "dotnet-app");

            // Step 1: save STS row in the headers map. Passes.
            const string step1 = """
                {
                  "changes": {
                    "security-headers": {
                      "headers": { "Strict-Transport-Security": "max-age=3600" }
                    }
                  }
                }
                """;

            (await PutSettingsRawAsync(slug, step1)).StatusCode.ShouldBe(HttpStatusCode.OK);

            // Step 2: toggle enableHsts. The delta carries no headers; the
            // in-flight check alone would PASS this. The merged-state check
            // sees the persisted STS row + the new enableHsts:true and
            // rejects -- this is the load-bearing assertion.
            const string step2 = """
                {
                  "changes": {
                    "security-headers": {
                      "enableHsts": true
                    }
                  }
                }
                """;

            using var response = await PutSettingsRawAsync(slug, step2);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var body = await response.Content.ReadAsStringAsync();
            body.ShouldContain("Strict-Transport-Security");
            body.ShouldContain("Enable HSTS");
        }
        finally
        {
            await DeleteAppAsync(slug);
        }
    }

    [Fact]
    public async Task PutSettings_TwoStep_EnableHstsThenStsInMap_Rejected()
    {
        // The symmetric two-step path. Step 1 toggles enableHsts (passes --
        // no STS in delta). Step 2 saves an STS row in the map (passes the
        // in-flight check -- delta carries no enableHsts). The merged-state
        // check fires on step 2 against {enableHsts:true, headers:{STS:...}}
        // and rejects. Asserting both directions guards against a one-sided
        // implementation that only checks the merged state when enableHsts
        // is in the in-flight delta.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"test-309-2step-rev-{suffix}";

        try
        {
            await CreateAppAsync(slug, "dotnet-app");

            // Step 1: toggle enableHsts on. Passes.
            const string step1 = """
                {
                  "changes": {
                    "security-headers": {
                      "enableHsts": true
                    }
                  }
                }
                """;

            (await PutSettingsRawAsync(slug, step1)).StatusCode.ShouldBe(HttpStatusCode.OK);

            // Step 2: try to add STS row to map. Must reject against merged.
            const string step2 = """
                {
                  "changes": {
                    "security-headers": {
                      "headers": { "Strict-Transport-Security": "max-age=3600" }
                    }
                  }
                }
                """;

            using var response = await PutSettingsRawAsync(slug, step2);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var body = await response.Content.ReadAsStringAsync();
            body.ShouldContain("Strict-Transport-Security");
            body.ShouldContain("Enable HSTS");
        }
        finally
        {
            await DeleteAppAsync(slug);
        }
    }

    private async Task CreateAppAsync(string slug, string appTypeSlug)
    {
        var payload = new
        {
            name = slug,
            displayName = $"Security Headers Test {slug}",
            appTypeSlug
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        request.Content = JsonContent.Create(payload, options: _jsonOptions);

        using var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private async Task<HttpResponseMessage> PutSettingsRawAsync(string slug, string rawJson)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/apps/{slug}/settings");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        request.Content = new StringContent(rawJson, Encoding.UTF8, "application/json");

        return await _client.SendAsync(request);
    }

    private async Task DeleteAppAsync(string slug)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/apps/{slug}");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        using var response = await _client.SendAsync(request);

        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            response.IsSuccessStatusCode.ShouldBeTrue();
        }
    }
}
