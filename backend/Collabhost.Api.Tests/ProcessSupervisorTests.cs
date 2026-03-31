using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests;

public class ProcessSupervisorTests(CollabhostApiFixture fixture) : IClassFixture<CollabhostApiFixture>
{
    private readonly CollabhostApiFixture _fixture = fixture;

    [Fact]
    public async Task StartApp_ValidApp_ReturnsRunningStatus()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "start-valid");

        // Act
        var response = await client.PostAsync($"/api/v1/apps/{externalId}/start", null);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("id").GetString().ShouldBe(externalId);

        var processRuntime = json.RootElement.GetProperty("runtime").GetProperty("process");
        processRuntime.GetProperty("state").GetString().ShouldBe("running");
        processRuntime.GetProperty("pid").GetInt32().ShouldBeGreaterThan(0);
        processRuntime.GetProperty("restartCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task StopApp_RunningApp_ReturnsStoppedStatus()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "stop-running");
        await client.PostAsync($"/api/v1/apps/{externalId}/start", null);

        // Act
        var response = await client.PostAsync($"/api/v1/apps/{externalId}/stop", null);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("id").GetString().ShouldBe(externalId);

        var processRuntime = json.RootElement.GetProperty("runtime").GetProperty("process");
        processRuntime.GetProperty("state").GetString().ShouldBe("stopped");
        processRuntime.GetProperty("pid").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task RestartApp_RunningApp_ReturnsRunningStatus()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "restart-running");
        await client.PostAsync($"/api/v1/apps/{externalId}/start", null);

        // Act
        var response = await client.PostAsync($"/api/v1/apps/{externalId}/restart", null);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("id").GetString().ShouldBe(externalId);

        var processRuntime = json.RootElement.GetProperty("runtime").GetProperty("process");
        processRuntime.GetProperty("state").GetString().ShouldBe("running");
        processRuntime.GetProperty("pid").GetInt32().ShouldBeGreaterThan(0);
        processRuntime.GetProperty("restartCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task GetStatus_NeverStartedApp_ReturnsStopped()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "status-never-started");

        // Act
        var response = await client.GetAsync($"/api/v1/apps/{externalId}/status");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("externalId").GetString().ShouldBe(externalId);
        json.RootElement.GetProperty("processState").GetString().ShouldBe("Stopped");
        json.RootElement.GetProperty("pid").ValueKind.ShouldBe(JsonValueKind.Null);
        json.RootElement.GetProperty("startedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        json.RootElement.GetProperty("restartCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task StartApp_AlreadyRunning_Returns409()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "start-already-running");
        await client.PostAsync($"/api/v1/apps/{externalId}/start", null);

        // Act
        var response = await client.PostAsync($"/api/v1/apps/{externalId}/start", null);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task StopApp_AlreadyStopped_Returns409()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "stop-already-stopped");
        await client.PostAsync($"/api/v1/apps/{externalId}/start", null);
        await client.PostAsync($"/api/v1/apps/{externalId}/stop", null);

        // Act
        var response = await client.PostAsync($"/api/v1/apps/{externalId}/stop", null);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task StartApp_StaticSite_ReturnsOkWithRouteOnly()
    {
        // Arrange — static sites have no process, but Start enables route
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "start-static-site", staticSite: true);

        // Act
        var response = await client.PostAsync($"/api/v1/apps/{externalId}/start", null);

        // Assert — should succeed (route is enabled)
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("runtime").GetProperty("process").ValueKind.ShouldBe(JsonValueKind.Null);
        json.RootElement.GetProperty("runtime").GetProperty("route").GetProperty("state").GetString().ShouldBe("active");
    }

    [Fact]
    public async Task GetStatus_NonexistentApp_Returns404()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/v1/apps/nonexistent-app-id/status");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static string ToTitleCase(string input)
    {
        var words = input.Split('-', ' ');
        return string.Join(" ", words.Select(w => char.ToUpper(w[0], CultureInfo.InvariantCulture) + w[1..]));
    }

    private static object CreateValidRequest(string name, bool staticSite = false) =>
        new
        {
            Name = name,
            DisplayName = $"{ToTitleCase(name)} App",
            AppTypeId = staticSite
                ? TestCatalogConstants.AppTypes.StaticSiteExternalId
                : TestCatalogConstants.AppTypes.ExecutableExternalId
        };

    private static async Task<string> CreateAppAsync(HttpClient client, string name, bool staticSite = false)
    {
        var request = CreateValidRequest(name, staticSite);
        var response = await client.PostAsJsonAsync("/api/v1/apps", request);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("externalId").GetString()!;
    }
}
