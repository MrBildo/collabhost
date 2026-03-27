using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Services;
using Collabhost.Api.Tests.Fixtures;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests;

public class LogRetrievalTests(CollabhostApiFixture fixture) : IClassFixture<CollabhostApiFixture>
{
    private readonly CollabhostApiFixture _fixture = fixture;

    [Fact]
    public async Task GetLogs_RunningApp_ReturnsEntries()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "logs-running");
        await client.PostAsync($"/api/v1/apps/{externalId}/start", null);

        var runner = _fixture.Services.GetRequiredService<IManagedProcessRunner>() as FakeProcessRunner;
        runner!.LastHandle!.EmitOutput("line one", LogStream.StdOut);
        runner.LastHandle.EmitOutput("line two", LogStream.StdOut);
        runner.LastHandle.EmitOutput("line three", LogStream.StdOut);

        // Act
        var response = await client.GetAsync($"/api/v1/apps/{externalId}/logs");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var entries = json.RootElement.GetProperty("entries");
        entries.GetArrayLength().ShouldBe(3);

        entries[0].GetProperty("content").GetString().ShouldBe("line one");
        entries[0].GetProperty("stream").GetString().ShouldBe("StdOut");
        entries[0].GetProperty("timestamp").GetString().ShouldNotBeNullOrWhiteSpace();

        entries[1].GetProperty("content").GetString().ShouldBe("line two");
        entries[2].GetProperty("content").GetString().ShouldBe("line three");

        json.RootElement.GetProperty("totalBuffered").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task GetLogs_NeverStartedApp_ReturnsEmpty()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "logs-never-started");

        // Act
        var response = await client.GetAsync($"/api/v1/apps/{externalId}/logs");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("entries").GetArrayLength().ShouldBe(0);
        json.RootElement.GetProperty("totalBuffered").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task GetLogs_WithStreamFilter_ReturnsFilteredEntries()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "logs-stream-filter");
        await client.PostAsync($"/api/v1/apps/{externalId}/start", null);

        var runner = _fixture.Services.GetRequiredService<IManagedProcessRunner>() as FakeProcessRunner;
        runner!.LastHandle!.EmitOutput("stdout one", LogStream.StdOut);
        runner.LastHandle.EmitOutput("stderr one", LogStream.StdErr);
        runner.LastHandle.EmitOutput("stdout two", LogStream.StdOut);
        runner.LastHandle.EmitOutput("stderr two", LogStream.StdErr);

        // Act
        var response = await client.GetAsync($"/api/v1/apps/{externalId}/logs?stream=stderr");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var entries = json.RootElement.GetProperty("entries");
        entries.GetArrayLength().ShouldBe(2);

        entries[0].GetProperty("content").GetString().ShouldBe("stderr one");
        entries[0].GetProperty("stream").GetString().ShouldBe("StdErr");
        entries[1].GetProperty("content").GetString().ShouldBe("stderr two");
        entries[1].GetProperty("stream").GetString().ShouldBe("StdErr");
    }

    [Fact]
    public async Task GetLogs_WithCountParam_LimitsResults()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "logs-count-limit");
        await client.PostAsync($"/api/v1/apps/{externalId}/start", null);

        var runner = _fixture.Services.GetRequiredService<IManagedProcessRunner>() as FakeProcessRunner;
        for (var i = 1; i <= 5; i++)
        {
            runner!.LastHandle!.EmitOutput($"line {i}", LogStream.StdOut);
        }

        // Act
        var response = await client.GetAsync($"/api/v1/apps/{externalId}/logs?count=2");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var entries = json.RootElement.GetProperty("entries");
        entries.GetArrayLength().ShouldBe(2);

        entries[0].GetProperty("content").GetString().ShouldBe("line 4");
        entries[1].GetProperty("content").GetString().ShouldBe("line 5");

        json.RootElement.GetProperty("totalBuffered").GetInt32().ShouldBe(5);
    }

    [Fact]
    public async Task GetLogs_NonexistentApp_Returns404()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/v1/apps/nonexistent-app-id/logs");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetLogs_InvalidStream_Returns400()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "logs-invalid-stream");

        // Act
        var response = await client.GetAsync($"/api/v1/apps/{externalId}/logs?stream=invalid");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
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
                ? IdentifierCatalog.AppTypes.StaticSite
                : IdentifierCatalog.AppTypes.Executable,
            InstallDirectory = $"C:\\apps\\{name}",
            CommandLine = $"{name}.exe",
            Arguments = (string?)null,
            WorkingDirectory = (string?)null,
            RestartPolicyId = IdentifierCatalog.RestartPolicies.Never,
            HealthEndpoint = (string?)null,
            UpdateCommand = (string?)null,
            AutoStart = false
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
