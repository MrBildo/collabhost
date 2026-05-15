using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Registry;

[Collection("Api")]
public class SettingsSchemaOverrideTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private const string _expectedSystemServiceHelpText =
        "Passed to Caddy's child process at startup. Use this for runtime secrets (e.g. CLOUDFLARE_API_TOKEN). "
        + "Not the same as Caddy's {env.NAME} JSON-config substitution syntax -- "
        + "that's configured in the proxy-config seam.";

    [Fact]
    public async Task GetSettings_DotNetApp_VariablesUsesBaseHelpText()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"test-schema-base-{suffix}";

        try
        {
            await CreateAppAsync(slug, "dotnet-app");

            var helpText = await GetVariablesHelpTextAsync(slug);

            // The base FieldDescriptor on EnvironmentConfiguration ships generic copy applicable
            // to every binding without a schemaOverride. dotnet-app has no schemaOverride for this
            // capability, so the generic copy must come through unchanged.
            helpText.ShouldNotBeNull();
            helpText.ShouldContain("application's child process");
            helpText.ShouldNotContain("Caddy");
        }
        finally
        {
            await DeleteAppAsync(slug);
        }
    }

    [Fact]
    public async Task GetSettings_SystemServiceApp_VariablesUsesCaddySpecificHelpText()
    {
        // The proxy app is the canonical system-service in production -- its environment-defaults
        // binding declares a schemaOverride that swaps the generic helpText for Caddy-specific copy.
        // Test fixture cannot rely on proxy auto-seed (no Caddy binary on the test runner), so we
        // register a fresh system-service app here. The schemaOverride is bound to the AppType, so
        // every system-service app receives the override -- the proxy is just the production user.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"test-schema-override-{suffix}";

        try
        {
            await CreateAppAsync(slug, "system-service");

            var helpText = await GetVariablesHelpTextAsync(slug);

            helpText.ShouldBe(_expectedSystemServiceHelpText);
        }
        finally
        {
            await DeleteAppAsync(slug);
        }
    }

    [Fact]
    public async Task GetSettings_DotNetApp_OtherFieldsUnaffectedByOverrides()
    {
        // Sanity-check that the schema-override path does not perturb fields it does not touch.
        // dotnet-app has no schemaOverrides; we expect the full base FieldDescriptor projection.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"test-schema-untouched-{suffix}";

        try
        {
            await CreateAppAsync(slug, "dotnet-app");

            using var settingsRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{slug}/settings");
            settingsRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            using var settingsResponse = await _client.SendAsync(settingsRequest);

            settingsResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await settingsResponse.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);

            var envSection = FindSection(document.RootElement, "environment-defaults");

            envSection.ShouldNotBeNull();

            var variablesField = FindField(envSection.Value, "variables");

            variablesField.ShouldNotBeNull();

            // Untouched fields hold base values
            variablesField.Value.GetProperty("label").GetString().ShouldBe("Environment Variables");
            variablesField.Value.GetProperty("requiresRestart").GetBoolean().ShouldBeTrue();
            variablesField.Value.GetProperty("type").GetString().ShouldBe("keyvalue");
        }
        finally
        {
            await DeleteAppAsync(slug);
        }
    }

    // ----- Card #308: per-field key-pattern hint on the settings DTO -----

    [Fact]
    public async Task GetSettings_StaticSite_ResponseHeadersFieldCarriesKeyPatternHintAndSeededDefault()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"test-308-headers-{suffix}";

        try
        {
            await CreateAppAsync(slug, "static-site");

            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{slug}/settings");
            request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            using var response = await _client.SendAsync(request);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);

            var routingSection = FindSection(document.RootElement, "routing");
            routingSection.ShouldNotBeNull();

            var responseHeadersField = FindField(routingSection.Value, "responseHeaders");
            responseHeadersField.ShouldNotBeNull();

            responseHeadersField.Value.GetProperty("type").GetString().ShouldBe("keyvalue");

            // The key-pattern hint is present and is the server-authoritative pattern.
            var keyPattern = responseHeadersField.Value.GetProperty("keyPattern").GetString();
            keyPattern.ShouldNotBeNull();
            keyPattern.ShouldBe(@"^/[^\s:]+::[!#$%&'*+.^_`|~0-9A-Za-z-]+$");

            var keyPatternMessage = responseHeadersField.Value.GetProperty("keyPatternMessage").GetString();
            keyPatternMessage.ShouldNotBeNull();
            keyPatternMessage.ShouldContain("<path>::<HeaderName>");

            // The seeded default ships the Collaboard config.json rule with zero operator action.
            var defaultValue = responseHeadersField.Value.GetProperty("defaultValue");
            defaultValue.GetProperty("/config.json::Cache-Control").GetString().ShouldBe("no-cache");
        }
        finally
        {
            await DeleteAppAsync(slug);
        }
    }

    [Fact]
    public async Task GetSettings_EnvironmentVariablesField_HasNoKeyPatternHint()
    {
        // The absent-hint contract: env-var KeyValue fields declare no
        // KeyPattern, so the DTO carries keyPattern=null. The frontend reads
        // null-or-absent as "keep the env-var default" -- existing fields
        // byte-for-byte unaffected.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"test-308-envkey-{suffix}";

        try
        {
            await CreateAppAsync(slug, "dotnet-app");

            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{slug}/settings");
            request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            using var response = await _client.SendAsync(request);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);

            var envSection = FindSection(document.RootElement, "environment-defaults");
            envSection.ShouldNotBeNull();

            var variablesField = FindField(envSection.Value, "variables");
            variablesField.ShouldNotBeNull();

            // keyPattern is present-but-null (absent-hint contract).
            variablesField.Value.GetProperty("keyPattern").ValueKind.ShouldBe(JsonValueKind.Null);
            variablesField.Value.GetProperty("keyPatternMessage").ValueKind.ShouldBe(JsonValueKind.Null);
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
            displayName = $"Schema Override Test {slug}",
            appTypeSlug
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        request.Content = JsonContent.Create(payload, options: _jsonOptions);

        using var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private async Task DeleteAppAsync(string slug)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/apps/{slug}");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        using var response = await _client.SendAsync(request);

        // Tolerate 404 in finally blocks if the app was never created.
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            response.IsSuccessStatusCode.ShouldBeTrue();
        }
    }

    private async Task<string?> GetVariablesHelpTextAsync(string slug)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{slug}/settings");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        using var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        var envSection = FindSection(document.RootElement, "environment-defaults");

        envSection.ShouldNotBeNull($"Settings for app '{slug}' should include environment-defaults section");

        var variablesField = FindField(envSection.Value, "variables");

        variablesField.ShouldNotBeNull("environment-defaults should include variables field");

        return variablesField.Value.TryGetProperty("helpText", out var helpTextElement)
            && helpTextElement.ValueKind == JsonValueKind.String
                ? helpTextElement.GetString()
                : null;
    }

    private static JsonElement? FindSection(JsonElement root, string sectionKey)
    {
        foreach (var section in root.GetProperty("sections").EnumerateArray())
        {
            if (string.Equals(section.GetProperty("key").GetString(), sectionKey, StringComparison.Ordinal))
            {
                return section;
            }
        }

        return null;
    }

    private static JsonElement? FindField(JsonElement section, string fieldKey)
    {
        foreach (var field in section.GetProperty("fields").EnumerateArray())
        {
            if (string.Equals(field.GetProperty("key").GetString(), fieldKey, StringComparison.Ordinal))
            {
                return field;
            }
        }

        return null;
    }
}
