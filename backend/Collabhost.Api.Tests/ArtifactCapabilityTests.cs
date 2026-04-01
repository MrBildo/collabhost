using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

using static Collabhost.Api.Tests.Fixtures.AppTestHelpers;

namespace Collabhost.Api.Tests;

public class ArtifactCapabilityTests(CollabhostApiFixture fixture) : IClassFixture<CollabhostApiFixture>
{
    private readonly CollabhostApiFixture _fixture = fixture;

    [Fact]
    public async Task CreateApp_WithArtifactOverride_PersistsLocation()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var tempDir = CreateTempDirectory();
        var externalId = await CreateAppAsync(client, "artifact-persist", artifactLocation: tempDir);

        // Act
        var response = await client.GetAsync($"/api/v1/apps/{externalId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        var artifactCapability = json.RootElement.GetProperty("capabilities").GetProperty("artifact");
        artifactCapability.GetProperty("hasOverrides").GetBoolean().ShouldBeTrue();
        artifactCapability.GetProperty("resolved").GetProperty("location").GetString().ShouldBe(tempDir);
    }

    [Fact]
    public async Task CreateApp_DefaultArtifact_HasEmptyLocation()
    {
        // Arrange — the default artifact config for all app types has empty location
        var client = _fixture.CreateAuthenticatedClient();
        var tempDir = CreateTempDirectory();
        var externalId = await CreateAppAsync(client, "artifact-default", artifactLocation: tempDir);

        // Act
        var response = await client.GetAsync($"/api/v1/apps/{externalId}");

        // Assert
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        var artifactCapability = json.RootElement.GetProperty("capabilities").GetProperty("artifact");
        var defaultConfig = artifactCapability.GetProperty("defaults").GetProperty("location").GetString();
        defaultConfig.ShouldBeEmpty();
    }

    [Fact]
    public async Task CreateApp_WithoutArtifactOverride_Returns400()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var request = new
        {
            Name = "artifact-missing",
            DisplayName = "Artifact Missing App",
            AppTypeId = TestCatalogConstants.AppTypes.ExecutableExternalId
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/apps", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateApp_WithEmptyArtifactLocation_Returns400()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var request = new
        {
            Name = "artifact-empty",
            DisplayName = "Artifact Empty App",
            AppTypeId = TestCatalogConstants.AppTypes.ExecutableExternalId,
            CapabilityOverrides = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["artifact"] = new { location = "" }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/apps", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateApp_WithNonexistentArtifactLocation_Returns400()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var request = new
        {
            Name = "artifact-noexist",
            DisplayName = "Artifact Nonexistent App",
            AppTypeId = TestCatalogConstants.AppTypes.ExecutableExternalId,
            CapabilityOverrides = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["artifact"] = new { location = @"C:\nonexistent-path-that-surely-does-not-exist" }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/apps", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
