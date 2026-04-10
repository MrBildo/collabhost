using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Mcp;
using Collabhost.Api.Probes;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;
using Collabhost.Api.Supervisor;
using Collabhost.Api.Tests.Fixtures;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ModelContextProtocol.Protocol;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Mcp;

// Representative MCP tool tests calling tool classes directly via DI.
// The MCP HTTP transport uses SSE streaming which is incompatible with TestHost's
// synchronous content buffering. We test the tool logic against the real DI container
// and database to cover the end-to-end behavior from tool invocation to AppStore/Supervisor.
[Collection("Api")]
public class McpToolTests(ApiFixture fixture)
{
    private readonly IServiceProvider _services = fixture.Services;
    private readonly HttpClient _client = fixture.Client;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Registers a minimal static-site app for use in tests that need an existing app slug.
    // static-site has no process requirement so it's safe to create in integration tests.
    private async Task<string> CreateTestAppAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"mcp-test-{suffix}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        request.Content = JsonContent.Create
        (
            new
            {
                name = slug,
                displayName = "MCP Test App",
                appTypeId = "static-site"
            },
            options: _jsonOptions
        );

        var response = await _client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return slug;
    }

    private async Task DeleteTestAppAsync(string slug)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/apps/{slug}");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        await _client.SendAsync(request);
    }

    private DiscoveryTools CreateDiscoveryTools()
    {
        var appStore = _services.GetRequiredService<AppStore>();
        var typeStore = _services.GetRequiredService<TypeStore>();
        var supervisor = _services.GetRequiredService<ProcessSupervisor>();
        var proxy = _services.GetRequiredService<ProxyManager>();
        var probeService = _services.GetRequiredService<ProbeService>();

        return new DiscoveryTools(appStore, typeStore, supervisor, proxy, probeService);
    }

    private LifecycleTools CreateLifecycleTools()
    {
        var appStore = _services.GetRequiredService<AppStore>();
        var typeStore = _services.GetRequiredService<TypeStore>();
        var supervisor = _services.GetRequiredService<ProcessSupervisor>();
        var proxy = _services.GetRequiredService<ProxyManager>();
        var activityEventStore = _services.GetRequiredService<ActivityEventStore>();
        var logger = _services.GetRequiredService<ILogger<LifecycleTools>>();

        // ICurrentUser is scoped -- create a scope to resolve it
        using var scope = _services.CreateScope();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUser>();

        return new LifecycleTools(appStore, typeStore, supervisor, proxy, currentUser, activityEventStore, logger);
    }

    private ConfigurationTools CreateConfigurationTools()
    {
        var appStore = _services.GetRequiredService<AppStore>();
        var typeStore = _services.GetRequiredService<TypeStore>();
        var supervisor = _services.GetRequiredService<ProcessSupervisor>();
        var proxy = _services.GetRequiredService<ProxyManager>();
        var proxySettings = _services.GetRequiredService<ProxySettings>();
        var activityEventStore = _services.GetRequiredService<ActivityEventStore>();
        var logger = _services.GetRequiredService<ILogger<ConfigurationTools>>();

        // ICurrentUser is scoped -- create a scope to resolve it
        using var scope = _services.CreateScope();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUser>();

        return new ConfigurationTools(appStore, typeStore, supervisor, proxy, proxySettings, currentUser, activityEventStore, logger);
    }

    private RegistrationTools CreateRegistrationTools()
    {
        var appStore = _services.GetRequiredService<AppStore>();
        var typeStore = _services.GetRequiredService<TypeStore>();
        var supervisor = _services.GetRequiredService<ProcessSupervisor>();
        var proxy = _services.GetRequiredService<ProxyManager>();
        var activityEventStore = _services.GetRequiredService<ActivityEventStore>();
        var logger = _services.GetRequiredService<ILogger<RegistrationTools>>();

        // ICurrentUser is scoped -- create a scope to resolve it
        using var scope = _services.CreateScope();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUser>();

        return new RegistrationTools(appStore, typeStore, supervisor, proxy, currentUser, activityEventStore, logger);
    }

    // -------- Discovery: list_apps --------

    [Fact]
    public async Task ListApps_HappyPath_ReturnsSuccessResult()
    {
        var tools = CreateDiscoveryTools();

        var result = await tools.ListAppsAsync(null, CancellationToken.None);

        (result.IsError ?? false).ShouldBeFalse();
        result.Content.ShouldNotBeEmpty();
        result.Content[0].ShouldBeOfType<TextContentBlock>();
    }

    [Fact]
    public async Task ListApps_WithStatusFilter_ReturnsNonErrorResult()
    {
        var tools = CreateDiscoveryTools();

        var result = await tools.ListAppsAsync("running", CancellationToken.None);

        (result.IsError ?? false).ShouldBeFalse();
    }

    // -------- Lifecycle: get_logs --------

    [Fact]
    public async Task GetLogs_ExistingApp_ReturnsSuccessWithLogHeader()
    {
        var slug = await CreateTestAppAsync();

        try
        {
            var tools = CreateLifecycleTools();

            var result = await tools.GetLogsAsync(slug, null, null, CancellationToken.None);

            (result.IsError ?? false).ShouldBeFalse();

            var text = GetFirstText(result);

            text.ShouldContain(slug);
        }
        finally
        {
            await DeleteTestAppAsync(slug);
        }
    }

    [Fact]
    public async Task GetLogs_UnknownApp_ReturnsError()
    {
        var tools = CreateLifecycleTools();

        var result = await tools.GetLogsAsync("no-such-app-xyz", null, null, CancellationToken.None);

        (result.IsError ?? false).ShouldBeTrue();

        var text = GetFirstText(result);

        text.ShouldContain("no-such-app-xyz");
    }

    // -------- Configuration: get_settings --------

    [Fact]
    public async Task GetSettings_ExistingApp_ReturnsSettingsSections()
    {
        var slug = await CreateTestAppAsync();

        try
        {
            var tools = CreateConfigurationTools();

            var result = await tools.GetSettingsAsync(slug, CancellationToken.None);

            (result.IsError ?? false).ShouldBeFalse();

            var text = GetFirstText(result);

            text.ShouldNotBeNullOrEmpty();
        }
        finally
        {
            await DeleteTestAppAsync(slug);
        }
    }

    [Fact]
    public async Task GetSettings_UnknownApp_ReturnsError()
    {
        var tools = CreateConfigurationTools();

        var result = await tools.GetSettingsAsync("no-such-app-abc", CancellationToken.None);

        (result.IsError ?? false).ShouldBeTrue();

        var text = GetFirstText(result);

        text.ShouldContain("no-such-app-abc");
    }

    // -------- Registration: detect_strategy --------

    [Fact]
    public void DetectStrategy_ExistingDirectory_ReturnsNonErrorResponse()
    {
        var dir = Path.GetTempPath();

        var result = RegistrationTools.DetectStrategy(dir, "dotnet-app");

        (result.IsError ?? false).ShouldBeFalse();
    }

    [Fact]
    public void DetectStrategy_NonexistentDirectory_ReturnsError()
    {
        var result = RegistrationTools.DetectStrategy("/does/not/exist/xyz-abc", "dotnet-app");

        (result.IsError ?? false).ShouldBeTrue();
    }

    // -------- Registration: register_app persists artifact location --------

    [Fact]
    public async Task RegisterApp_WithInstallDirectory_PersistsArtifactLocation()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var appName = $"mcp-artifact-test-{suffix}";
        var installDirectory = Path.Combine(Path.GetTempPath(), $"collabhost-test-{suffix}");

        Directory.CreateDirectory(installDirectory);

        try
        {
            var tools = CreateRegistrationTools();

            var result = await tools.RegisterAppAsync
            (
                appName,
                "executable",
                installDirectory,
                null,
                CancellationToken.None
            );

            (result.IsError ?? false).ShouldBeFalse("Registration should succeed");

            // Read settings back via the REST API and verify artifact.location
            using var settingsRequest = new HttpRequestMessage
            (
                HttpMethod.Get,
                $"/api/v1/apps/{appName}/settings"
            );

            settingsRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var settingsResponse = await _client.SendAsync(settingsRequest);

            settingsResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            var settingsJson = await settingsResponse.Content.ReadAsStringAsync();
            var settings = JsonDocument.Parse(settingsJson);

            var sections = settings.RootElement.GetProperty("sections");

            var artifactSection = sections.EnumerateArray()
                .Single
                (
                    s => string.Equals
                    (
                        s.GetProperty("key").GetString(),
                        "artifact",
                        StringComparison.Ordinal
                    )
                );

            var locationField = artifactSection.GetProperty("fields").EnumerateArray()
                .Single
                (
                    f => string.Equals
                    (
                        f.GetProperty("key").GetString(),
                        "location",
                        StringComparison.Ordinal
                    )
                );

            locationField.GetProperty("value").GetString().ShouldBe
            (
                installDirectory,
                "artifact.location should be persisted from installDirectory during MCP registration"
            );
        }
        finally
        {
            await DeleteTestAppAsync(appName);

            if (Directory.Exists(installDirectory))
            {
                Directory.Delete(installDirectory, recursive: true);
            }
        }
    }

    // -------- Auth rejection at HTTP layer --------

    [Fact]
    public async Task McpEndpoint_NoKey_ReturnsUnauthorized()
    {
        var client = fixture.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Content = new StringContent
        (
            """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""",
            System.Text.Encoding.UTF8,
            "application/json"
        );
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task McpEndpoint_InvalidKey_ReturnsUnauthorized()
    {
        var client = fixture.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Content = new StringContent
        (
            """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""",
            System.Text.Encoding.UTF8,
            "application/json"
        );
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        request.Headers.Add("X-User-Key", "01BADKEYNOTINDB000000000X");

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static string GetFirstText(CallToolResult result) =>
        result.Content is { Count: > 0 } && result.Content[0] is TextContentBlock textBlock
            ? textBlock.Text
            : string.Empty;
}
