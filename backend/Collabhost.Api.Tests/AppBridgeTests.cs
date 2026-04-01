using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

using static Collabhost.Api.Tests.Fixtures.AppTestHelpers;

namespace Collabhost.Api.Tests;

public class AppBridgeTests(CollabhostApiFixture fixture) : IClassFixture<CollabhostApiFixture>
{
    private readonly CollabhostApiFixture _fixture = fixture;

    [Fact]
    public async Task GetApp_ReturnsCapabilities()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "bridge-get-test");

        // Act
        var response = await client.GetAsync($"/api/v1/apps/{externalId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        // Verify app type reference
        var appType = json.RootElement.GetProperty("appType");
        appType.GetProperty("name").GetString().ShouldBe("executable");
        appType.GetProperty("displayName").GetString().ShouldBe("Executable");

        // Verify capabilities
        var capabilities = json.RootElement.GetProperty("capabilities");
        capabilities.GetProperty("process").GetProperty("category").GetString().ShouldBe("behavioral");
        capabilities.GetProperty("process").GetProperty("hasOverrides").GetBoolean().ShouldBeFalse();

        // Verify runtime section exists
        json.RootElement.TryGetProperty("runtime", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task GetApp_RuntimeProcess_NullWhenNotStarted()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "bridge-runtime-null");

        // Act
        var response = await client.GetAsync($"/api/v1/apps/{externalId}");

        // Assert
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var runtime = json.RootElement.GetProperty("runtime");

        // Process section should be present but have no PID (not started)
        var process = runtime.GetProperty("process");
        process.GetProperty("state").GetString().ShouldBe("Stopped");
        process.GetProperty("pid").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetApp_RuntimeRoute_HasDomainAndState()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "bridge-route");

        // Act
        var response = await client.GetAsync($"/api/v1/apps/{externalId}");

        // Assert
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var route = json.RootElement.GetProperty("runtime").GetProperty("route");
        route.GetProperty("domain").GetString().ShouldBe("bridge-route.collab.internal");
        route.GetProperty("state").GetString().ShouldBe("active");
    }

    [Fact]
    public async Task GetAllApps_ReturnsBridgeFormat()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        await CreateAppAsync(client, "bridge-list-one");

        // Act
        var response = await client.GetAsync("/api/v1/apps");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetArrayLength().ShouldBeGreaterThanOrEqualTo(1);

        var first = json.RootElement[0];
        first.TryGetProperty("appType", out _).ShouldBeTrue();
        first.TryGetProperty("runtime", out _).ShouldBeTrue();
        first.TryGetProperty("capabilities", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task CreateApp_WithCapabilityOverrides_PersistsOverrides()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var request = new
        {
            Name = "bridge-override-test",
            DisplayName = "Override Test App",
            AppTypeId = TestCatalogConstants.AppTypes.ExecutableExternalId,
            CapabilityOverrides = new Dictionary<string, object>
            {
                ["restart"] = new { policy = StringCatalog.RestartPolicies.Never }
            }
        };

        // Act
        var createResponse = await client.PostAsJsonAsync("/api/v1/apps", request);

        // Assert
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var createContent = await createResponse.Content.ReadAsStringAsync();
        var externalId = JsonDocument.Parse(createContent).RootElement.GetProperty("externalId").GetString()!;

        // Verify the override is reflected in the GET response
        var getResponse = await client.GetAsync($"/api/v1/apps/{externalId}");
        var getContent = await getResponse.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(getContent);

        var restartCapability = json.RootElement.GetProperty("capabilities").GetProperty("restart");
        restartCapability.GetProperty("hasOverrides").GetBoolean().ShouldBeTrue();
        restartCapability.GetProperty("resolved").GetProperty("policy").GetString().ShouldBe(StringCatalog.RestartPolicies.Never);
    }

    [Fact]
    public async Task CreateApp_WithInvalidOverrideSlug_Returns400()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var request = new
        {
            Name = "bridge-invalid-slug",
            DisplayName = "Invalid Slug App",
            AppTypeId = TestCatalogConstants.AppTypes.ExecutableExternalId,
            CapabilityOverrides = new Dictionary<string, object>
            {
                ["nonexistent-capability"] = new { foo = "bar" }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/apps", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateApp_WithOverrideForWrongType_Returns400()
    {
        // Arrange — Executable does not have health-check capability
        var client = _fixture.CreateAuthenticatedClient();
        var request = new
        {
            Name = "bridge-wrong-type",
            DisplayName = "Wrong Type App",
            AppTypeId = TestCatalogConstants.AppTypes.ExecutableExternalId,
            CapabilityOverrides = new Dictionary<string, object>
            {
                ["health-check"] = new { endpoint = "/healthz" }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/apps", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateApp_WithCapabilityOverrides_PersistsOverrides()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "bridge-update-override");

        // Act — set an override
        var updateResponse = await client.PutAsJsonAsync($"/api/v1/apps/{externalId}", new
        {
            CapabilityOverrides = new Dictionary<string, object>
            {
                ["restart"] = new { policy = StringCatalog.RestartPolicies.Always }
            }
        });

        // Assert
        updateResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var getResponse = await client.GetAsync($"/api/v1/apps/{externalId}");
        var content = await getResponse.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("capabilities").GetProperty("restart")
            .GetProperty("hasOverrides").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task StopApp_StaticSite_DisablesRoute()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "bridge-stop-static", staticSite: true);

        // Act
        var response = await client.PostAsync($"/api/v1/apps/{externalId}/stop", null);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("runtime").GetProperty("route")
            .GetProperty("state").GetString().ShouldBe("disabled");
    }

    [Fact]
    public async Task KillApp_NoProcess_Returns400()
    {
        // Arrange — static site has no process
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "bridge-kill-noproc", staticSite: true);

        // Act
        var response = await client.PostAsync($"/api/v1/apps/{externalId}/kill", null);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task KillApp_NotRunning_Returns400()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "bridge-kill-notrun");

        // Act
        var response = await client.PostAsync($"/api/v1/apps/{externalId}/kill", null);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

}
