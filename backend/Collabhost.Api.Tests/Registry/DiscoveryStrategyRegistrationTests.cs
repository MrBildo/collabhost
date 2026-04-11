using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Registry;

[Collection("Api")]
public class DiscoveryStrategyRegistrationTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // dotnet-app type slug (Phase 1b: CreateAppAsync now accepts slugs)
    private const string _dotNetAppTypeSlug = "dotnet-app";

    [Fact]
    public async Task RegisterWithDiscoveryStrategyOverride_AppliesProcessOverride()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"test-discovery-{suffix}";

        try
        {
            var createPayload = new
            {
                name = slug,
                displayName = "Discovery Strategy Test",
                appTypeSlug = _dotNetAppTypeSlug,
                values = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["discovery"] = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["discoveryStrategy"] = "DotNetProject"
                    }
                }
            };

            using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
            createRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
            createRequest.Content = JsonContent.Create(createPayload, options: _jsonOptions);

            var createResponse = await _client.SendAsync(createRequest);

            createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

            // Verify the override was applied by checking settings
            using var settingsRequest = new HttpRequestMessage
            (
                HttpMethod.Get,
                $"/api/v1/apps/{slug}/settings"
            );

            settingsRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var settingsResponse = await _client.SendAsync(settingsRequest);

            settingsResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await settingsResponse.Content.ReadAsStringAsync();
            var settings = JsonDocument.Parse(body).RootElement;

            var processSection = FindByKey(settings.GetProperty("sections"), "process");

            processSection.ShouldNotBeNull("Settings should include a process section");

            var strategyField = FindByKey
            (
                processSection.Value.GetProperty("fields"),
                "discoveryStrategy"
            );

            strategyField.ShouldNotBeNull("Process section should include discoveryStrategy field");

            // The effective value should be the overridden one, not the default
            // JSON enum values are PascalCase in the stored configuration
            strategyField.Value.GetProperty("value").GetString()
                .ShouldBe("DotNetProject");
        }
        finally
        {
            using var deleteRequest = new HttpRequestMessage
            (
                HttpMethod.Delete,
                $"/api/v1/apps/{slug}"
            );

            deleteRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            await _client.SendAsync(deleteRequest);
        }
    }

    [Fact]
    public async Task RegisterWithoutDiscoveryOverride_UsesDefaultStrategy()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"test-default-disc-{suffix}";

        try
        {
            var createPayload = new
            {
                name = slug,
                displayName = "Default Strategy Test",
                appTypeSlug = _dotNetAppTypeSlug
            };

            using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
            createRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
            createRequest.Content = JsonContent.Create(createPayload, options: _jsonOptions);

            var createResponse = await _client.SendAsync(createRequest);

            createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

            // Verify default strategy is preserved
            using var settingsRequest = new HttpRequestMessage
            (
                HttpMethod.Get,
                $"/api/v1/apps/{slug}/settings"
            );

            settingsRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var settingsResponse = await _client.SendAsync(settingsRequest);

            settingsResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await settingsResponse.Content.ReadAsStringAsync();
            var settings = JsonDocument.Parse(body).RootElement;

            var processSection = FindByKey(settings.GetProperty("sections"), "process");

            processSection.ShouldNotBeNull();

            var strategyField = FindByKey
            (
                processSection.Value.GetProperty("fields"),
                "discoveryStrategy"
            );

            strategyField.ShouldNotBeNull();

            // The effective value should be the seed default (DotNetRuntimeConfiguration for dotnet-app)
            // JSON enum values are PascalCase in the stored configuration
            strategyField.Value.GetProperty("value").GetString()
                .ShouldBe("DotNetRuntimeConfiguration");
        }
        finally
        {
            using var deleteRequest = new HttpRequestMessage
            (
                HttpMethod.Delete,
                $"/api/v1/apps/{slug}"
            );

            deleteRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            await _client.SendAsync(deleteRequest);
        }
    }

    private static JsonElement? FindByKey(JsonElement array, string key)
    {
        foreach (var item in array.EnumerateArray())
        {
            if (string.Equals(item.GetProperty("key").GetString(), key, StringComparison.Ordinal))
            {
                return item;
            }
        }

        return null;
    }
}
