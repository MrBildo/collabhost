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

public class AppUpdateTests(CollabhostApiFixture fixture) : IClassFixture<CollabhostApiFixture>
{
    private readonly CollabhostApiFixture _fixture = fixture;

    [Fact]
    public async Task RunUpdate_WasRunning_StopsUpdatesAndRestarts()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppWithUpdateCommandAsync(client, "update-was-running");
        await client.PostAsync($"/api/v1/apps/{externalId}/start", null);

        var runner = _fixture.Services.GetRequiredService<IManagedProcessRunner>() as FakeProcessRunner;
        runner!.NextRunResult = new ProcessRunResult(0, false);
        var callCountBefore = runner.RunToCompletionCallCount;

        // Act
        var response = await client.PostAsync($"/api/v1/apps/{externalId}/update", null);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/event-stream");

        runner.RunToCompletionCallCount.ShouldBe(callCountBefore + 1);
        runner.LastRunConfig.ShouldNotBeNull();
        runner.LastRunConfig.Arguments.ShouldNotBeNull();
        runner.LastRunConfig.Arguments.ShouldContain("git pull");

        var body = await response.Content.ReadAsStringAsync();
        var events = ParseSseEvents(body);

        events.ShouldContain(e => e.EventType == "status" && e.Data.Contains("\"stopping\""));
        events.ShouldContain(e => e.EventType == "status" && e.Data.Contains("\"updating\""));
        events.ShouldContain(e => e.EventType == "status" && e.Data.Contains("\"starting\""));
        events.ShouldContain(e => e.EventType == "status" && e.Data.Contains("\"complete\""));
        events.ShouldContain(e => e.EventType == "log");
        events.ShouldContain(e => e.EventType == "result" && e.Data.Contains("\"success\":true"));

        // Verify the app was restarted (Start was called after RunToCompletion)
        runner.LastHandle.ShouldNotBeNull();
    }

    [Fact]
    public async Task RunUpdate_WasStopped_UpdatesWithoutRestart()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppWithUpdateCommandAsync(client, "update-was-stopped");

        var runner = _fixture.Services.GetRequiredService<IManagedProcessRunner>() as FakeProcessRunner;
        runner!.NextRunResult = new ProcessRunResult(0, false);
        var callCountBefore = runner.RunToCompletionCallCount;

        // Act
        var response = await client.PostAsync($"/api/v1/apps/{externalId}/update", null);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        runner.RunToCompletionCallCount.ShouldBe(callCountBefore + 1);

        var body = await response.Content.ReadAsStringAsync();
        var events = ParseSseEvents(body);

        events.ShouldNotContain(e => e.EventType == "status" && e.Data.Contains("\"stopping\""));
        events.ShouldNotContain(e => e.EventType == "status" && e.Data.Contains("\"starting\""));
        events.ShouldContain(e => e.EventType == "status" && e.Data.Contains("\"updating\""));
        events.ShouldContain(e => e.EventType == "status" && e.Data.Contains("\"complete\""));
        events.ShouldContain(e => e.EventType == "result" && e.Data.Contains("\"success\":true"));
    }

    [Fact]
    public async Task RunUpdate_FailedUpdate_DoesNotRestart()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppWithUpdateCommandAsync(client, "update-failed");
        await client.PostAsync($"/api/v1/apps/{externalId}/start", null);

        var runner = _fixture.Services.GetRequiredService<IManagedProcessRunner>() as FakeProcessRunner;
        runner!.NextRunResult = new ProcessRunResult(1, false);

        // Act
        var response = await client.PostAsync($"/api/v1/apps/{externalId}/update", null);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var events = ParseSseEvents(body);

        events.ShouldNotContain(e => e.EventType == "status" && e.Data.Contains("\"starting\""));
        events.ShouldContain(e => e.EventType == "status" && e.Data.Contains("\"failed\""));
        events.ShouldContain(e => e.EventType == "result" && e.Data.Contains("\"success\":false"));
        events.ShouldContain(e => e.EventType == "result" && e.Data.Contains("\"exitCode\":1"));
    }

    [Fact]
    public async Task RunUpdate_NoUpdateCommand_Returns400()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "update-no-command");

        // Act
        var response = await client.PostAsync($"/api/v1/apps/{externalId}/update", null);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        content.ShouldContain("NO_UPDATE_COMMAND");
    }

    [Fact]
    public async Task RunUpdate_AppNotFound_Returns404()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.PostAsync("/api/v1/apps/nonexistent-app-id/update", null);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RunUpdate_TimedOut_ReportsTimeout()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppWithUpdateCommandAsync(client, "update-timeout");

        var runner = _fixture.Services.GetRequiredService<IManagedProcessRunner>() as FakeProcessRunner;
        runner!.NextRunResult = new ProcessRunResult(-1, true);

        // Act
        var response = await client.PostAsync($"/api/v1/apps/{externalId}/update", null);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var events = ParseSseEvents(body);

        events.ShouldContain(e => e.EventType == "result" && e.Data.Contains("\"timedOut\":true"));
        events.ShouldContain(e => e.EventType == "result" && e.Data.Contains("\"success\":false"));
    }

    private static string ToTitleCase(string input)
    {
        var words = input.Split('-', ' ');
        return string.Join(" ", words.Select(w => char.ToUpper(w[0], CultureInfo.InvariantCulture) + w[1..]));
    }

    private static object CreateValidRequest(string name, string? updateCommand = null) =>
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
            UpdateCommand = updateCommand,
            UpdateTimeoutSeconds = (int?)null,
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

    private static async Task<string> CreateAppWithUpdateCommandAsync(HttpClient client, string name)
    {
        var request = CreateValidRequest(name, "git pull");
        var response = await client.PostAsJsonAsync("/api/v1/apps", request);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("externalId").GetString()!;
    }

    private sealed record SseEvent(string EventType, string Data);

    private static List<SseEvent> ParseSseEvents(string body)
    {
        var events = new List<SseEvent>();
        var lines = body.Split('\n');

        string? currentEvent = null;
        string? currentData = null;

        foreach (var line in lines)
        {
            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                currentEvent = line["event: ".Length..].Trim();
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                currentData = line["data: ".Length..].Trim();
            }
            else if (line.Trim() == "" && currentEvent is not null && currentData is not null)
            {
                events.Add(new SseEvent(currentEvent, currentData));
                currentEvent = null;
                currentData = null;
            }
        }

        return events;
    }
}
