using System.Net;
using System.Text.Json;

using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests;

public class CapabilityEndpointTests(CollabhostApiFixture fixture) : IClassFixture<CollabhostApiFixture>
{
    private readonly CollabhostApiFixture _fixture = fixture;

    [Fact]
    public async Task GetAllCapabilities_ReturnsSeededCapabilities()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/v1/capabilities");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetArrayLength().ShouldBe(10);
    }

    [Fact]
    public async Task GetAllCapabilities_IncludesExpectedFields()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/v1/capabilities");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var first = json.RootElement[0];
        first.TryGetProperty("slug", out _).ShouldBeTrue();
        first.TryGetProperty("displayName", out _).ShouldBeTrue();
        first.TryGetProperty("category", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task GetAllCapabilities_ContainsProcessCapability()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/v1/capabilities");

        // Assert
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        var hasProcess = false;
        foreach (var element in json.RootElement.EnumerateArray())
        {
            if (element.GetProperty("slug").GetString() == StringCatalog.Capabilities.Process)
            {
                hasProcess = true;
                element.GetProperty("category").GetString().ShouldBe("behavioral");
                element.GetProperty("displayName").GetString().ShouldBe("Process Management");
                break;
            }
        }

        hasProcess.ShouldBeTrue();
    }

    [Fact]
    public async Task GetFieldOptions_ReturnsOk()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/v1/capabilities/field-options");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.TryGetProperty("fieldOptions", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task GetFieldOptions_ContainsRestartPolicyOptions()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/v1/capabilities/field-options");

        // Assert
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var fieldOptions = json.RootElement.GetProperty("fieldOptions");

        var restartField = FindFieldOptionSet(fieldOptions, StringCatalog.Capabilities.Restart, "policy");
        restartField.ShouldNotBeNull();

        var options = restartField.Value.GetProperty("options");
        options.GetArrayLength().ShouldBe(3);

        var values = options.EnumerateArray()
            .Select(o => o.GetProperty("value").GetString())
            .ToList();
        values.ShouldContain(StringCatalog.RestartPolicies.Never);
        values.ShouldContain(StringCatalog.RestartPolicies.OnCrash);
        values.ShouldContain(StringCatalog.RestartPolicies.Always);
    }

    [Fact]
    public async Task GetFieldOptions_ContainsServeModeOptions()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/v1/capabilities/field-options");

        // Assert
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var fieldOptions = json.RootElement.GetProperty("fieldOptions");

        var serveModeField = FindFieldOptionSet(fieldOptions, StringCatalog.Capabilities.Routing, "serveMode");
        serveModeField.ShouldNotBeNull();

        var options = serveModeField.Value.GetProperty("options");
        options.GetArrayLength().ShouldBe(2);

        var values = options.EnumerateArray()
            .Select(o => o.GetProperty("value").GetString())
            .ToList();
        values.ShouldContain(StringCatalog.ServeModes.ReverseProxy);
        values.ShouldContain(StringCatalog.ServeModes.FileServer);
    }

    [Fact]
    public async Task GetFieldOptions_ContainsDiscoveryStrategyOptions()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/v1/capabilities/field-options");

        // Assert
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var fieldOptions = json.RootElement.GetProperty("fieldOptions");

        var discoveryField = FindFieldOptionSet(fieldOptions, StringCatalog.Capabilities.Process, "discoveryStrategy");
        discoveryField.ShouldNotBeNull();

        var options = discoveryField.Value.GetProperty("options");
        options.GetArrayLength().ShouldBe(3);

        var values = options.EnumerateArray()
            .Select(o => o.GetProperty("value").GetString())
            .ToList();
        values.ShouldContain(StringCatalog.DiscoveryStrategies.DotNetRuntimeConfig);
        values.ShouldContain(StringCatalog.DiscoveryStrategies.PackageJson);
        values.ShouldContain(StringCatalog.DiscoveryStrategies.Manual);
    }

    [Fact]
    public async Task GetFieldOptions_OptionsIncludeDisplayNames()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/v1/capabilities/field-options");

        // Assert
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var fieldOptions = json.RootElement.GetProperty("fieldOptions");

        var restartField = FindFieldOptionSet(fieldOptions, StringCatalog.Capabilities.Restart, "policy");
        restartField.ShouldNotBeNull();

        var firstOption = restartField.Value.GetProperty("options")[0];
        firstOption.TryGetProperty("value", out _).ShouldBeTrue();
        firstOption.TryGetProperty("displayName", out _).ShouldBeTrue();
        firstOption.GetProperty("displayName").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    private static JsonElement? FindFieldOptionSet
    (
        JsonElement fieldOptions,
        string capabilitySlug,
        string fieldName
    )
    {
        foreach (var element in fieldOptions.EnumerateArray())
        {
            if (element.GetProperty("capabilitySlug").GetString() == capabilitySlug
                && element.GetProperty("fieldName").GetString() == fieldName)
            {
                return element;
            }
        }

        return null;
    }
}
