using System.Net;
using System.Text.Json;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Registry;

[Collection("Api")]
public class RegistrationSchemaTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    [Fact]
    public async Task StaticSiteRegistration_IncludesSpaFallbackField()
    {
        using var request = new HttpRequestMessage
        (
            HttpMethod.Get, "/api/v1/app-types/static-site/registration"
        );
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var schema = JsonDocument.Parse(body).RootElement;

        var sections = schema.GetProperty("sections");
        var routingSection = FindByKey(sections, "routing");

        routingSection.ShouldNotBeNull("Static-site registration should include a routing section");

        routingSection.Value.GetProperty("title").GetString().ShouldBe("Routing Options");

        var fields = routingSection.Value.GetProperty("fields");
        var spaField = FindByKey(fields, "spaFallback");

        spaField.ShouldNotBeNull("Routing section should include a spaFallback field");
        spaField.Value.GetProperty("type").GetString().ShouldBe("boolean");
        spaField.Value.GetProperty("required").GetBoolean().ShouldBeFalse();
        spaField.Value.GetProperty("defaultValue").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task DotNetAppRegistration_IncludesDiscoverySection()
    {
        using var request = new HttpRequestMessage
        (
            HttpMethod.Get, "/api/v1/app-types/dotnet-app/registration"
        );
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var schema = JsonDocument.Parse(body).RootElement;

        var sections = schema.GetProperty("sections");
        var discoverySection = FindByKey(sections, "discovery");

        discoverySection.ShouldNotBeNull("dotnet-app registration should include a discovery section");

        discoverySection.Value.GetProperty("title").GetString().ShouldBe("How to Run");

        var fields = discoverySection.Value.GetProperty("fields");
        var strategyField = FindByKey(fields, "discoveryStrategy");

        strategyField.ShouldNotBeNull("Discovery section should include discoveryStrategy field");
        strategyField.Value.GetProperty("type").GetString().ShouldBe("select");

        // Should include DotNetProject as an option
        var options = strategyField.Value.GetProperty("options");
        var hasProjectOption = false;

        foreach (var option in options.EnumerateArray())
        {
            if (string.Equals
            (
                option.GetProperty("value").GetString(),
                "dotNetProject",
                StringComparison.Ordinal
            ))
            {
                hasProjectOption = true;
            }
        }

        hasProjectOption.ShouldBeTrue("dotnet-app should include DotNetProject as a strategy option");
    }

    [Fact]
    public async Task StaticSiteRegistration_DoesNotIncludeDiscoverySection()
    {
        using var request = new HttpRequestMessage
        (
            HttpMethod.Get, "/api/v1/app-types/static-site/registration"
        );
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var schema = JsonDocument.Parse(body).RootElement;

        var sections = schema.GetProperty("sections");
        var discoverySection = FindByKey(sections, "discovery");

        discoverySection.ShouldBeNull("static-site should not include a discovery section");
    }

    [Fact]
    public async Task DotNetAppRegistration_DoesNotIncludeSpaFallback()
    {
        using var request = new HttpRequestMessage
        (
            HttpMethod.Get, "/api/v1/app-types/dotnet-app/registration"
        );
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var schema = JsonDocument.Parse(body).RootElement;

        var sections = schema.GetProperty("sections");
        var routingSection = FindByKey(sections, "routing");

        routingSection.ShouldBeNull("Non-file-server app types should not include a routing section");
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
