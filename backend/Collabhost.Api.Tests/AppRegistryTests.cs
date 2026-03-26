using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

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
        json.RootElement.GetProperty("externalId").GetString().ShouldBe(externalId);
        json.RootElement.GetProperty("name").GetString().ShouldBe("get-test");
        json.RootElement.GetProperty("displayName").GetString().ShouldBe("Get Test App");
        json.RootElement.GetProperty("appTypeName").GetString().ShouldBe("Executable");
        json.RootElement.GetProperty("port").GetInt32().ShouldBeGreaterThan(0);
        json.RootElement.GetProperty("environmentVariables").GetArrayLength().ShouldBe(0);
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
            DisplayName = "Updated Display Name",
            InstallDirectory = "C:\\updated",
            CommandLine = "updated.exe",
            Arguments = (string?)null,
            WorkingDirectory = (string?)null,
            RestartPolicyId = IdentifierCatalog.RestartPolicies.Always,
            HealthEndpoint = (string?)null,
            UpdateCommand = (string?)null,
            AutoStart = true
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
        json.RootElement.GetProperty("autoStart").GetBoolean().ShouldBeTrue();
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
            AppTypeId = Guid.Empty,
            InstallDirectory = "",
            CommandLine = "",
            RestartPolicyId = Guid.Empty,
            AutoStart = false
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/apps", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
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

    private static string ToTitleCase(string input)
    {
        var words = input.Split('-', ' ');
        return string.Join(" ", words.Select(w => char.ToUpper(w[0]) + w[1..]));
    }

    private static object CreateValidRequest(string name) =>
        new
        {
            Name = name,
            DisplayName = $"{ToTitleCase(name)} App",
            AppTypeId = IdentifierCatalog.AppTypes.Executable,
            InstallDirectory = $"C:\\apps\\{name}",
            CommandLine = $"{name}.exe",
            Arguments = (string?)null,
            WorkingDirectory = (string?)null,
            RestartPolicyId = IdentifierCatalog.RestartPolicies.Never,
            HealthEndpoint = (string?)null,
            UpdateCommand = (string?)null,
            AutoStart = false
        };

    private static async Task<string> CreateAppAsync(HttpClient client, string name)
    {
        var request = CreateValidRequest(name);
        var response = await client.PostAsJsonAsync("/api/v1/apps", request);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("externalId").GetString()!;
    }
}
