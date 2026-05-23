using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Registry;

// Card #338 -- cross-tier propagation of FieldDescriptor.DependsOn. The wire
// DTO now carries the dependency metadata so the frontend can render dependent
// fields grayed + disabled + badged when the parent value does not satisfy
// the predicate. Effectiveness predicate, not legality predicate -- the value
// is allowed to sit inert; the runtime (Proxy builder / process runner) is
// what makes it inert in served behavior.
[Collection("Api")]
public class SettingsDependsOnTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task GetSettings_DotNetApp_HstsMaxAgeFieldCarriesBooleanDependsOn()
    {
        // security-headers/hstsMaxAgeSeconds declares DependsOn(enableHsts, "true").
        // Boolean parent: ToCamelCase("true") is "true" (no-op); the value comes
        // through verbatim.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"test-338-hsts-{suffix}";

        try
        {
            await CreateAppAsync(slug, "dotnet-app");

            var hstsMaxAgeField = await GetFieldAsync(slug, "security-headers", "hstsMaxAgeSeconds");

            hstsMaxAgeField.ShouldNotBeNull();

            hstsMaxAgeField.Value.TryGetProperty("dependsOn", out var dependsOnElement).ShouldBeTrue();
            dependsOnElement.ValueKind.ShouldBe(JsonValueKind.Object);
            dependsOnElement.GetProperty("field").GetString().ShouldBe("enableHsts");
            dependsOnElement.GetProperty("value").GetString().ShouldBe("true");
        }
        finally
        {
            await DeleteAppAsync(slug);
        }
    }

    [Fact]
    public async Task GetSettings_StaticSite_SpaFallbackFieldCarriesCamelCasedEnumDependsOn()
    {
        // routing/spaFallback declares DependsOn(serveMode, nameof(ServeMode.FileServer)).
        // Enum-name parent: ToCamelCase("FileServer") is "fileServer" -- aligns
        // with how Select options are camelCased at the same serialization
        // boundary so the FE comparator is a string-equality check.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"test-338-spa-{suffix}";

        try
        {
            await CreateAppAsync(slug, "static-site");

            var spaFallbackField = await GetFieldAsync(slug, "routing", "spaFallback");

            spaFallbackField.ShouldNotBeNull();

            spaFallbackField.Value.TryGetProperty("dependsOn", out var dependsOnElement).ShouldBeTrue();
            dependsOnElement.ValueKind.ShouldBe(JsonValueKind.Object);
            dependsOnElement.GetProperty("field").GetString().ShouldBe("serveMode");
            dependsOnElement.GetProperty("value").GetString().ShouldBe("fileServer");
        }
        finally
        {
            await DeleteAppAsync(slug);
        }
    }

    [Fact]
    public async Task GetSettings_DotNetApp_ProcessCommandCarriesCamelCasedDependsOn()
    {
        // process/command and process/arguments declare
        // DependsOn(discoveryStrategy, nameof(DiscoveryStrategy.Manual)).
        // The wire fix closes Marcus's catch -- these previously dropped the
        // metadata, so the FE rendered them unconditionally.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"test-338-process-{suffix}";

        try
        {
            await CreateAppAsync(slug, "dotnet-app");

            var commandField = await GetFieldAsync(slug, "process", "command");
            commandField.ShouldNotBeNull();
            commandField.Value.TryGetProperty("dependsOn", out var commandDep).ShouldBeTrue();
            commandDep.GetProperty("field").GetString().ShouldBe("discoveryStrategy");
            commandDep.GetProperty("value").GetString().ShouldBe("manual");

            var argumentsField = await GetFieldAsync(slug, "process", "arguments");
            argumentsField.ShouldNotBeNull();
            argumentsField.Value.TryGetProperty("dependsOn", out var argsDep).ShouldBeTrue();
            argsDep.GetProperty("field").GetString().ShouldBe("discoveryStrategy");
            argsDep.GetProperty("value").GetString().ShouldBe("manual");
        }
        finally
        {
            await DeleteAppAsync(slug);
        }
    }

    [Fact]
    public async Task GetSettings_FieldWithoutDeclaredDependency_OmitsDependsOnOrNullsIt()
    {
        // Absent-DependsOn contract: a field with no schema-declared dependency
        // serializes dependsOn as null (or absent). The FE treats null/absent as
        // "no dependency" -- every existing field is byte-for-byte unaffected.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"test-338-absent-{suffix}";

        try
        {
            await CreateAppAsync(slug, "dotnet-app");

            // enableHsts itself declares no DependsOn -- it is the parent of
            // hstsMaxAgeSeconds, not a dependent.
            var enableHstsField = await GetFieldAsync(slug, "security-headers", "enableHsts");

            enableHstsField.ShouldNotBeNull();

            // Either the property is absent OR present and null. Both shapes
            // resolve to "no dependency" on the FE side; we accept both.
            if (enableHstsField.Value.TryGetProperty("dependsOn", out var dependsOnElement))
            {
                dependsOnElement.ValueKind.ShouldBe(JsonValueKind.Null);
            }
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
            displayName = $"DependsOn Test {slug}",
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

        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            response.IsSuccessStatusCode.ShouldBeTrue();
        }
    }

    private async Task<JsonElement?> GetFieldAsync(string slug, string sectionKey, string fieldKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{slug}/settings");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        using var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        foreach (var section in document.RootElement.GetProperty("sections").EnumerateArray())
        {
            if (!string.Equals(section.GetProperty("key").GetString(), sectionKey, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var field in section.GetProperty("fields").EnumerateArray())
            {
                if (string.Equals(field.GetProperty("key").GetString(), fieldKey, StringComparison.Ordinal))
                {
                    // Need to clone since the document is disposed.
                    return JsonDocument.Parse(field.GetRawText()).RootElement;
                }
            }
        }

        return null;
    }
}
