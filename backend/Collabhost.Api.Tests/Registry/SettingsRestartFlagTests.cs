using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Registry;

[Collection("Api")]
public class SettingsRestartFlagTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task GetSettings_DotNetApp_FlagsRestartRequiredFields()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"test-restart-{suffix}";

        try
        {
            // Register a .NET app (has all capabilities)
            var createPayload = new
            {
                name = slug,
                displayName = "Restart Flag Test App",
                appTypeId = "01KN8K1MRQ0K06ADYJJ8VAXG5Y"
            };

            using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
            createRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
            createRequest.Content = JsonContent.Create(createPayload, options: _jsonOptions);

            var createResponse = await _client.SendAsync(createRequest);

            createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

            // Fetch settings
            using var settingsRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{slug}/settings");
            settingsRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var settingsResponse = await _client.SendAsync(settingsRequest);

            settingsResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await settingsResponse.Content.ReadAsStringAsync();
            var settings = JsonDocument.Parse(body).RootElement;
            var sections = settings.GetProperty("sections");

            // Fields that SHOULD require restart
            var expectedRestartRequired = new HashSet<string>(StringComparer.Ordinal)
            {
                "process.discoveryStrategy",
                "process.command",
                "process.arguments",
                "process.workingDirectory",
                "port-injection.environmentVariableName",
                "port-injection.portFormat",
                "routing.domainPattern",
                "routing.serveMode",
                "routing.spaFallback",
                "artifact.location",
                "environment-defaults.variables",
            };

            // Fields that should NOT require restart
            var expectedNoRestart = new HashSet<string>(StringComparer.Ordinal)
            {
                "identity.name",
                "identity.displayName",
                "process.shutdownTimeoutSeconds",
                "process.startupGracePeriodSeconds",
                "process.maxStartupRetries",
                "health-check.endpoint",
                "health-check.intervalSeconds",
                "health-check.timeoutSeconds",
                "restart.policy",
                "restart.successExitCodes",
                "auto-start.enabled",
            };

            var checkedFields = new HashSet<string>(StringComparer.Ordinal);

            foreach (var section in sections.EnumerateArray())
            {
                var sectionKey = section.GetProperty("key").GetString()!;

                foreach (var field in section.GetProperty("fields").EnumerateArray())
                {
                    var fieldKey = field.GetProperty("key").GetString()!;
                    var fullKey = $"{sectionKey}.{fieldKey}";
                    var requiresRestart = field.GetProperty("requiresRestart").GetBoolean();

                    if (expectedRestartRequired.Contains(fullKey))
                    {
                        requiresRestart.ShouldBeTrue($"Field '{fullKey}' should require restart");
                        checkedFields.Add(fullKey);
                    }
                    else if (expectedNoRestart.Contains(fullKey))
                    {
                        requiresRestart.ShouldBeFalse($"Field '{fullKey}' should NOT require restart");
                        checkedFields.Add(fullKey);
                    }
                }
            }

            // Verify all expected fields were found in the response
            foreach (var expected in expectedRestartRequired)
            {
                checkedFields.ShouldContain(expected, $"Expected restart-required field '{expected}' was not found in settings response");
            }

            foreach (var expected in expectedNoRestart)
            {
                checkedFields.ShouldContain(expected, $"Expected no-restart field '{expected}' was not found in settings response");
            }
        }
        finally
        {
            using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/apps/{slug}");
            deleteRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            await _client.SendAsync(deleteRequest);
        }
    }

    [Fact]
    public async Task GetSettings_StaticSite_FlagsRoutingFieldsAsRestartRequired()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"test-static-restart-{suffix}";

        try
        {
            // Register a static site (routing + artifact only)
            var createPayload = new
            {
                name = slug,
                displayName = "Static Site Restart Test",
                appTypeId = "01KN8K1MRT26VCX65J1ZSVWESB"
            };

            using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
            createRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
            createRequest.Content = JsonContent.Create(createPayload, options: _jsonOptions);

            var createResponse = await _client.SendAsync(createRequest);

            createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

            // Fetch settings
            using var settingsRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{slug}/settings");
            settingsRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var settingsResponse = await _client.SendAsync(settingsRequest);

            settingsResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await settingsResponse.Content.ReadAsStringAsync();
            var settings = JsonDocument.Parse(body).RootElement;
            var sections = settings.GetProperty("sections");

            // Find routing section
            var routingSection = FindSectionByKey(sections, "routing");

            routingSection.ShouldNotBeNull("Static site should have a routing section");

            var spaFallbackField = FindFieldByKey(routingSection.Value, "spaFallback");

            spaFallbackField.ShouldNotBeNull("Routing section should have spaFallback field");
            spaFallbackField.Value.GetProperty("requiresRestart").GetBoolean().ShouldBeTrue();

            // Find artifact section
            var artifactSection = FindSectionByKey(sections, "artifact");

            artifactSection.ShouldNotBeNull("Static site should have an artifact section");

            var locationField = FindFieldByKey(artifactSection.Value, "location");

            locationField.ShouldNotBeNull("Artifact section should have location field");
            locationField.Value.GetProperty("requiresRestart").GetBoolean().ShouldBeTrue();
        }
        finally
        {
            using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/apps/{slug}");
            deleteRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            await _client.SendAsync(deleteRequest);
        }
    }

    [Fact]
    public async Task GetSettings_IdentityFields_NeverRequireRestart()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"test-identity-restart-{suffix}";

        try
        {
            var createPayload = new
            {
                name = slug,
                displayName = "Identity Restart Test",
                appTypeId = "01KN8K1MRQ0K06ADYJJ8VAXG5Y"
            };

            using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
            createRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
            createRequest.Content = JsonContent.Create(createPayload, options: _jsonOptions);

            var createResponse = await _client.SendAsync(createRequest);

            createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

            using var settingsRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{slug}/settings");
            settingsRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var settingsResponse = await _client.SendAsync(settingsRequest);
            var body = await settingsResponse.Content.ReadAsStringAsync();
            var settings = JsonDocument.Parse(body).RootElement;
            var sections = settings.GetProperty("sections");

            var identitySection = FindSectionByKey(sections, "identity");

            identitySection.ShouldNotBeNull("Settings should have an identity section");

            foreach (var field in identitySection.Value.GetProperty("fields").EnumerateArray())
            {
                var fieldKey = field.GetProperty("key").GetString();

                field.GetProperty("requiresRestart").GetBoolean()
                    .ShouldBeFalse($"Identity field '{fieldKey}' should never require restart");
            }
        }
        finally
        {
            using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/apps/{slug}");
            deleteRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            await _client.SendAsync(deleteRequest);
        }
    }

    private static JsonElement? FindSectionByKey(JsonElement sections, string key)
    {
        foreach (var section in sections.EnumerateArray())
        {
            if (string.Equals(section.GetProperty("key").GetString(), key, StringComparison.Ordinal))
            {
                return section;
            }
        }

        return null;
    }

    private static JsonElement? FindFieldByKey(JsonElement section, string key)
    {
        foreach (var field in section.GetProperty("fields").EnumerateArray())
        {
            if (string.Equals(field.GetProperty("key").GetString(), key, StringComparison.Ordinal))
            {
                return field;
            }
        }

        return null;
    }
}
