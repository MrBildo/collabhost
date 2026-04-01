using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

using static Collabhost.Api.Tests.Fixtures.AppTestHelpers;

namespace Collabhost.Api.Tests;

public class AppRegistryTests(CollabhostApiFixture fixture) : IClassFixture<CollabhostApiFixture>
{
    private readonly CollabhostApiFixture _fixture = fixture;

    [Fact]
    public async Task CreateApp_ReturnsCreatedWithExternalId()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var request = CreateValidRequest("create-test");

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/apps", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("externalId").GetString().ShouldNotBeNullOrWhiteSpace();
        response.Headers.Location.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetApp_ReturnsCorrectData()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "get-test");

        // Act
        var response = await client.GetAsync($"/api/v1/apps/{externalId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("id").GetString().ShouldBe(externalId);
        json.RootElement.GetProperty("name").GetString().ShouldBe("get-test");
        json.RootElement.GetProperty("displayName").GetString().ShouldBe("Get Test App");
        json.RootElement.GetProperty("appType").GetProperty("displayName").GetString().ShouldBe("Executable");
    }

    [Fact]
    public async Task ListApps_ReturnsArray()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        await CreateAppAsync(client, "list-test-1");
        await CreateAppAsync(client, "list-test-2");

        // Act
        var response = await client.GetAsync("/api/v1/apps");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetArrayLength().ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task UpdateApp_ReturnsNoContent()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "update-test");

        var updateRequest = new
        {
            DisplayName = "Updated Display Name"
        };

        // Act
        var response = await client.PutAsJsonAsync($"/api/v1/apps/{externalId}", updateRequest);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Verify the update persisted
        var getResponse = await client.GetAsync($"/api/v1/apps/{externalId}");
        var content = await getResponse.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("displayName").GetString().ShouldBe("Updated Display Name");
    }

    [Fact]
    public async Task DeleteApp_ReturnsNoContent()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "delete-test");

        // Act
        var response = await client.DeleteAsync($"/api/v1/apps/{externalId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GetDeletedApp_ReturnsNotFound()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "delete-then-get");
        await client.DeleteAsync($"/api/v1/apps/{externalId}");

        // Act
        var response = await client.GetAsync($"/api/v1/apps/{externalId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateApp_WithMissingRequiredFields_ReturnsBadRequest()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var request = new
        {
            Name = "",
            DisplayName = "",
            AppTypeId = ""
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/apps", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateApp_WithEmptyName_ReturnsBadRequest()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var request = new
        {
            Name = "",
            DisplayName = "Some Display Name",
            AppTypeId = TestCatalogConstants.AppTypes.ExecutableExternalId
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/apps", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateApp_WithEmptyDisplayName_ReturnsBadRequest()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var request = new
        {
            Name = "valid-slug",
            DisplayName = "",
            AppTypeId = TestCatalogConstants.AppTypes.ExecutableExternalId
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/apps", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateApp_WithInvalidSlugCharacters_ReturnsBadRequest()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var request = new
        {
            Name = "My App!",
            DisplayName = "My App",
            AppTypeId = TestCatalogConstants.AppTypes.ExecutableExternalId
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/apps", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateApp_WithValidSlug_ReturnsCreated()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var request = CreateValidRequest("valid-slug-test");

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/apps", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateApp_WithDuplicateName_ReturnsBadRequest()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        await CreateAppAsync(client, "dupe-name-test");

        var duplicateRequest = CreateValidRequest("dupe-name-test");

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/apps", duplicateRequest);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateApp_WithoutAuthKey_Returns403()
    {
        // Arrange
        var client = _fixture.CreateClient();
        var request = CreateValidRequest("no-auth-test");

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/apps", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

}
