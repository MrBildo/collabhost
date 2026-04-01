using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests;

public class AppTypeEndpointTests(CollabhostApiFixture fixture) : IClassFixture<CollabhostApiFixture>
{
    private readonly CollabhostApiFixture _fixture = fixture;

    [Fact]
    public async Task GetAllAppTypes_ReturnsBuiltInTypes()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/v1/app-types");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetArrayLength().ShouldBeGreaterThanOrEqualTo(5);
    }

    [Fact]
    public async Task GetAppType_BuiltIn_ReturnsWithCapabilities()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync(
            $"/api/v1/app-types/{TestCatalogConstants.AppTypes.ExecutableExternalId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("name").GetString().ShouldBe("executable");
        json.RootElement.GetProperty("displayName").GetString().ShouldBe("Executable");
        json.RootElement.GetProperty("isBuiltIn").GetBoolean().ShouldBeTrue();

        var capabilities = json.RootElement.GetProperty("capabilities");
        capabilities.GetProperty("process").GetProperty("category").GetString().ShouldBe("behavioral");
        capabilities.GetProperty("routing").GetProperty("category").GetString().ShouldBe("behavioral");
    }

    [Fact]
    public async Task GetAppType_NotFound_Returns404()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/v1/app-types/nonexistent");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateAppType_ValidRequest_ReturnsCreated()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var request = new
        {
            Name = "custom-type-test",
            DisplayName = "Custom Type Test",
            Description = "A test custom type",
            Capabilities = new Dictionary<string, object>
            {
                ["routing"] = new
                {
                    domainPattern = "{slug}.collab.internal",
                    serveMode = StringCatalog.ServeModes.ReverseProxy
                }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/app-types", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("externalId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CreateAppType_DuplicateName_Returns400()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var request = new
        {
            Name = "dupe-type-test",
            DisplayName = "Dupe Type"
        };

        await client.PostAsJsonAsync("/api/v1/app-types", request);

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/app-types", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateAppType_UnknownCapability_Returns400()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var request = new
        {
            Name = "unknown-cap-test",
            DisplayName = "Unknown Capability Test",
            Capabilities = new Dictionary<string, object>
            {
                ["nonexistent-capability"] = new { foo = "bar" }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/app-types", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateAppType_ValidRequest_ReturnsNoContent()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var createResponse = await client.PostAsJsonAsync("/api/v1/app-types", new
        {
            Name = "update-type-test",
            DisplayName = "Original Name"
        });
        createResponse.EnsureSuccessStatusCode();
        var createContent = await createResponse.Content.ReadAsStringAsync();
        var externalId = JsonDocument.Parse(createContent).RootElement.GetProperty("externalId").GetString()!;

        // Act
        var response = await client.PutAsJsonAsync($"/api/v1/app-types/{externalId}", new
        {
            DisplayName = "Updated Name",
            Description = "Updated description"
        });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Verify
        var getResponse = await client.GetAsync($"/api/v1/app-types/{externalId}");
        var getContent = await getResponse.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(getContent);
        json.RootElement.GetProperty("displayName").GetString().ShouldBe("Updated Name");
        json.RootElement.GetProperty("description").GetString().ShouldBe("Updated description");
    }

    [Fact]
    public async Task DeleteAppType_BuiltIn_Returns409()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.DeleteAsync(
            $"/api/v1/app-types/{TestCatalogConstants.AppTypes.ExecutableExternalId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeleteAppType_CustomWithNoApps_ReturnsNoContent()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var createResponse = await client.PostAsJsonAsync("/api/v1/app-types", new
        {
            Name = "delete-type-test",
            DisplayName = "Delete Type Test"
        });
        createResponse.EnsureSuccessStatusCode();
        var createContent = await createResponse.Content.ReadAsStringAsync();
        var externalId = JsonDocument.Parse(createContent).RootElement.GetProperty("externalId").GetString()!;

        // Act
        var response = await client.DeleteAsync($"/api/v1/app-types/{externalId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteAppType_WithReferencingApps_Returns409()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Create a custom type with routing capability
        var createTypeResponse = await client.PostAsJsonAsync("/api/v1/app-types", new
        {
            Name = "delete-ref-type",
            DisplayName = "Delete Ref Type",
            Capabilities = new Dictionary<string, object>
            {
                ["routing"] = new
                {
                    domainPattern = "{slug}.collab.internal",
                    serveMode = StringCatalog.ServeModes.ReverseProxy
                }
            }
        });
        createTypeResponse.EnsureSuccessStatusCode();
        var typeContent = await createTypeResponse.Content.ReadAsStringAsync();
        var typeExternalId = JsonDocument.Parse(typeContent).RootElement.GetProperty("externalId").GetString()!;

        // Create an app using that type
        await client.PostAsJsonAsync("/api/v1/apps", new
        {
            Name = "delete-ref-app",
            DisplayName = "Delete Ref App",
            AppTypeId = typeExternalId
        });

        // Act
        var response = await client.DeleteAsync($"/api/v1/app-types/{typeExternalId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}
