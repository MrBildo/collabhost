using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Mcp;
using Collabhost.Api.Platform;
using Collabhost.Api.Probes;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;
using Collabhost.Api.StaticSite;
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
                appTypeSlug = "static-site"
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

    // Each Create*Tools method resolves the production DI services and constructs the tool
    // class. The McpRequestAuthenticator (Card #332) is also resolved from DI; tests pass
    // ApiFixture.AdminKey as the per-call authKey, which the authenticator resolves and
    // seeds CurrentUser from. The scope-shared services (CurrentUser, McpHeaderFallback)
    // come from a single test-scoped scope so the authenticator's CurrentUser.Set call is
    // visible to the tool body's _currentUser reads.

    private (DiscoveryTools Tools, IServiceScope Scope) CreateDiscoveryTools()
    {
        var scope = _services.CreateScope();
        var sp = scope.ServiceProvider;

        var tools = new DiscoveryTools
        (
            sp.GetRequiredService<IApplicationStartTime>(),
            sp.GetRequiredService<AppStore>(),
            sp.GetRequiredService<TypeStore>(),
            sp.GetRequiredService<ProcessSupervisor>(),
            sp.GetRequiredService<ProxyManager>(),
            sp.GetRequiredService<ProxySettings>(),
            sp.GetRequiredService<ProbeService>(),
            sp.GetRequiredService<AppDataPathResolver>(),
            sp.GetRequiredService<McpRequestAuthenticator>()
        );

        return (tools, scope);
    }

    private (LifecycleTools Tools, IServiceScope Scope) CreateLifecycleTools()
    {
        var scope = _services.CreateScope();
        var sp = scope.ServiceProvider;

        var tools = new LifecycleTools
        (
            sp.GetRequiredService<AppStore>(),
            sp.GetRequiredService<TypeStore>(),
            sp.GetRequiredService<ProcessSupervisor>(),
            sp.GetRequiredService<StartAppOperation>(),
            sp.GetRequiredService<StopAppOperation>(),
            sp.GetRequiredService<RestartAppOperation>(),
            sp.GetRequiredService<KillAppOperation>(),
            sp.GetRequiredService<McpRequestAuthenticator>()
        );

        return (tools, scope);
    }

    private (ConfigurationTools Tools, IServiceScope Scope) CreateConfigurationTools()
    {
        var scope = _services.CreateScope();
        var sp = scope.ServiceProvider;

        var tools = new ConfigurationTools
        (
            sp.GetRequiredService<AppStore>(),
            sp.GetRequiredService<TypeStore>(),
            sp.GetRequiredService<ProcessSupervisor>(),
            sp.GetRequiredService<ProxyManager>(),
            sp.GetRequiredService<ProxySettings>(),
            sp.GetRequiredService<ExternalTargetSettings>(),
            sp.GetRequiredService<RuntimeConfigFileWriter>(),
            sp.GetRequiredService<ICurrentUser>(),
            sp.GetRequiredService<ActivityEventStore>(),
            sp.GetRequiredService<McpRequestAuthenticator>(),
            sp.GetRequiredService<ILogger<ConfigurationTools>>()
        );

        return (tools, scope);
    }

    private (RegistrationTools Tools, IServiceScope Scope) CreateRegistrationTools()
    {
        var scope = _services.CreateScope();
        var sp = scope.ServiceProvider;

        var tools = new RegistrationTools
        (
            sp.GetRequiredService<AppStore>(),
            sp.GetRequiredService<TypeStore>(),
            sp.GetRequiredService<ProcessSupervisor>(),
            sp.GetRequiredService<ProxyManager>(),
            sp.GetRequiredService<ProxySettings>(),
            sp.GetRequiredService<ExternalTargetSettings>(),
            sp.GetRequiredService<ICurrentUser>(),
            sp.GetRequiredService<ActivityEventStore>(),
            sp.GetRequiredService<AppDataPathResolver>(),
            sp.GetRequiredService<McpRequestAuthenticator>(),
            sp.GetRequiredService<ILogger<RegistrationTools>>()
        );

        return (tools, scope);
    }

    // -------- Discovery: list_apps --------

    [Fact]
    public async Task ListApps_HappyPath_ReturnsSuccessResult()
    {
        var (tools, scope) = CreateDiscoveryTools();
        using var _ = scope;

        var result = await tools.ListAppsAsync(status: null, authKey: ApiFixture.AdminKey, CancellationToken.None);

        (result.IsError ?? false).ShouldBeFalse();
        result.Content.ShouldNotBeEmpty();
        result.Content[0].ShouldBeOfType<TextContentBlock>();
    }

    [Fact]
    public async Task ListApps_WithStatusFilter_ReturnsNonErrorResult()
    {
        var (tools, scope) = CreateDiscoveryTools();
        using var _ = scope;

        var result = await tools.ListAppsAsync(status: "running", authKey: ApiFixture.AdminKey, CancellationToken.None);

        (result.IsError ?? false).ShouldBeFalse();
    }

    // -------- Lifecycle: get_logs --------

    [Fact]
    public async Task GetLogs_ExistingApp_ReturnsSuccessWithLogHeader()
    {
        var slug = await CreateTestAppAsync();

        try
        {
            var (tools, scope) = CreateLifecycleTools();
            using var _ = scope;

            var result = await tools.GetLogsAsync(slug, limit: null, offset: null, authKey: ApiFixture.AdminKey, CancellationToken.None);

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
        var (tools, scope) = CreateLifecycleTools();
        using var _ = scope;

        var result = await tools.GetLogsAsync("no-such-app-xyz", limit: null, offset: null, authKey: ApiFixture.AdminKey, CancellationToken.None);

        (result.IsError ?? false).ShouldBeTrue();

        var text = GetFirstText(result);

        text.ShouldContain("no-such-app-xyz");
    }

    // -------- Lifecycle: start_app / stop_app (operation-spine adapters, #406 PR 3) --------
    //
    // start_app and stop_app now adapt the slug into a command, call the injected
    // StartAppOperation / StopAppOperation (dual-branch: routing-only vs process), and map the
    // result back. These direct-call tests are the MCP-observable oracle the migration must
    // preserve. A static-site (CreateTestAppAsync) is routing-only, so its start/stop runs the
    // routing-only branch end-to-end and returns the { slug, status, appType } success shape
    // byte-identical to pre-migration; the unknown-slug path keeps the MCP-surface AppNotFound shape
    // (kept above the operation, where REST returns an empty 404). The process branch's live success
    // needs a real running process (out of reach in this fixture); the routing-only success + the
    // not-found shape are what change here and what these pin.

    [Fact]
    public async Task StartApp_RoutingOnly_ReturnsRunningSuccessShape()
    {
        var slug = await CreateTestAppAsync();

        try
        {
            var (tools, scope) = CreateLifecycleTools();
            using var _ = scope;

            var result = await tools.StartAppAsync(slug, authKey: ApiFixture.AdminKey, CancellationToken.None);

            (result.IsError ?? false).ShouldBeFalse();

            var text = GetFirstText(result);

            text.ShouldContain(slug);
            text.ShouldContain("running");
            text.ShouldContain("static-site");
        }
        finally
        {
            await DeleteTestAppAsync(slug);
        }
    }

    [Fact]
    public async Task StartApp_UnknownApp_ReturnsAppNotFound()
    {
        var (tools, scope) = CreateLifecycleTools();
        using var _ = scope;

        var result = await tools.StartAppAsync("no-such-app-start", authKey: ApiFixture.AdminKey, CancellationToken.None);

        (result.IsError ?? false).ShouldBeTrue();

        var text = GetFirstText(result);

        text.ShouldContain("no-such-app-start");
    }

    [Fact]
    public async Task StopApp_RoutingOnly_ReturnsStoppedSuccessShape()
    {
        var slug = await CreateTestAppAsync();

        try
        {
            var (tools, scope) = CreateLifecycleTools();
            using var _ = scope;

            var result = await tools.StopAppAsync(slug, authKey: ApiFixture.AdminKey, CancellationToken.None);

            (result.IsError ?? false).ShouldBeFalse();

            var text = GetFirstText(result);

            text.ShouldContain(slug);
            text.ShouldContain("stopped");
            text.ShouldContain("static-site");
        }
        finally
        {
            await DeleteTestAppAsync(slug);
        }
    }

    [Fact]
    public async Task StopApp_UnknownApp_ReturnsAppNotFound()
    {
        var (tools, scope) = CreateLifecycleTools();
        using var _ = scope;

        var result = await tools.StopAppAsync("no-such-app-stop", authKey: ApiFixture.AdminKey, CancellationToken.None);

        (result.IsError ?? false).ShouldBeTrue();

        var text = GetFirstText(result);

        text.ShouldContain("no-such-app-stop");
    }

    // -------- Lifecycle: restart_app / kill_app (operation-spine adapters, #406 PR 2) --------
    //
    // restart_app and kill_app now adapt the slug into a command, call the injected
    // RestartAppOperation / KillAppOperation, and map the result back. These direct-call tests are
    // the MCP-observable oracle the migration must preserve: the "only process-based apps support
    // ..." Validation guard is an MCP-surface pre-check kept above the operation (REST has none),
    // and the unknown-slug path maps to AppNotFound. The success path on a LIVE process needs a
    // real running process (out of reach in this fixture, as the pre-migration suite's absence of
    // any restart/kill surface test reflects); the surface-guard + not-found shapes are what
    // change here and what these pin byte-identical to pre-migration.

    [Fact]
    public async Task RestartApp_StaticSite_ReturnsValidationErrorPreservingMcpMessage()
    {
        var slug = await CreateTestAppAsync();

        try
        {
            var (tools, scope) = CreateLifecycleTools();
            using var _ = scope;

            var result = await tools.RestartAppAsync(slug, authKey: ApiFixture.AdminKey, CancellationToken.None);

            (result.IsError ?? false).ShouldBeTrue();

            var text = GetFirstText(result);

            text.ShouldContain("only process-based apps support restart");
            text.ShouldContain(slug);
        }
        finally
        {
            await DeleteTestAppAsync(slug);
        }
    }

    [Fact]
    public async Task RestartApp_UnknownApp_ReturnsAppNotFound()
    {
        var (tools, scope) = CreateLifecycleTools();
        using var _ = scope;

        var result = await tools.RestartAppAsync("no-such-app-restart", authKey: ApiFixture.AdminKey, CancellationToken.None);

        (result.IsError ?? false).ShouldBeTrue();

        var text = GetFirstText(result);

        text.ShouldContain("no-such-app-restart");
    }

    [Fact]
    public async Task KillApp_StaticSite_ReturnsValidationErrorPreservingMcpMessage()
    {
        var slug = await CreateTestAppAsync();

        try
        {
            var (tools, scope) = CreateLifecycleTools();
            using var _ = scope;

            var result = await tools.KillAppAsync(slug, authKey: ApiFixture.AdminKey, CancellationToken.None);

            (result.IsError ?? false).ShouldBeTrue();

            var text = GetFirstText(result);

            text.ShouldContain("only process-based apps support kill");
            text.ShouldContain(slug);
        }
        finally
        {
            await DeleteTestAppAsync(slug);
        }
    }

    [Fact]
    public async Task KillApp_UnknownApp_ReturnsAppNotFound()
    {
        var (tools, scope) = CreateLifecycleTools();
        using var _ = scope;

        var result = await tools.KillAppAsync("no-such-app-kill", authKey: ApiFixture.AdminKey, CancellationToken.None);

        (result.IsError ?? false).ShouldBeTrue();

        var text = GetFirstText(result);

        text.ShouldContain("no-such-app-kill");
    }

    // -------- Configuration: get_settings --------

    [Fact]
    public async Task GetSettings_ExistingApp_ReturnsSettingsSections()
    {
        var slug = await CreateTestAppAsync();

        try
        {
            var (tools, scope) = CreateConfigurationTools();
            using var _ = scope;

            var result = await tools.GetSettingsAsync(slug, authKey: ApiFixture.AdminKey, CancellationToken.None);

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
        var (tools, scope) = CreateConfigurationTools();
        using var _ = scope;

        var result = await tools.GetSettingsAsync("no-such-app-abc", authKey: ApiFixture.AdminKey, CancellationToken.None);

        (result.IsError ?? false).ShouldBeTrue();

        var text = GetFirstText(result);

        text.ShouldContain("no-such-app-abc");
    }

    // -------- Registration: detect_strategy --------

    [Fact]
    public async Task DetectStrategy_ExistingDirectory_ReturnsNonErrorResponse()
    {
        var dir = Path.GetTempPath();

        var (tools, scope) = CreateRegistrationTools();
        using var _ = scope;

        var result = await tools.DetectStrategyAsync(dir, "dotnet-app", authKey: ApiFixture.AdminKey, CancellationToken.None);

        (result.IsError ?? false).ShouldBeFalse();
    }

    [Fact]
    public async Task DetectStrategy_NonexistentDirectory_ReturnsError()
    {
        var (tools, scope) = CreateRegistrationTools();
        using var _ = scope;

        var result = await tools.DetectStrategyAsync("/does/not/exist/xyz-abc", "dotnet-app", authKey: ApiFixture.AdminKey, CancellationToken.None);

        (result.IsError ?? false).ShouldBeTrue();
    }

    // Card #344: omitting appTypeSlug returns the per-type map shape (REST/MCP parity).
    [Fact]
    public async Task DetectStrategy_OmittedAppTypeSlug_ReturnsPerTypeMap()
    {
        var dir = Path.GetTempPath();

        var (tools, scope) = CreateRegistrationTools();
        using var _ = scope;

        var result = await tools.DetectStrategyAsync(dir, appTypeSlug: null, authKey: ApiFixture.AdminKey, CancellationToken.None);

        (result.IsError ?? false).ShouldBeFalse();

        var text = GetFirstText(result);

        // The shape should carry a perType object with one entry per
        // collector-known app type.
        text.ShouldContain("perType");
        text.ShouldContain("dotnet-app");
        text.ShouldContain("nodejs-app");
        text.ShouldContain("static-site");
        text.ShouldContain("executable");
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
            var (tools, scope) = CreateRegistrationTools();
            using var _ = scope;

            var result = await tools.RegisterAppAsync
            (
                appName,
                "executable",
                installDirectory,
                settings: null,
                authKey: ApiFixture.AdminKey,
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
                Directory.Delete(installDirectory, true);
            }
        }
    }

    // -------- Per-call auth contract (Card #332) --------
    //
    // Pre-#332: the /mcp endpoint enforced auth at session setup (HTTP 401 if X-User-Key was
    // missing/invalid). Card #332 moved auth enforcement to per-tool-call (the only channel
    // through which per-bot identity enters a shared user-scope MCP server). The /mcp endpoint
    // itself is now permissive at the HTTP layer; tool calls fail at the CallToolResult
    // layer when no key is supplied. tools/list does not require a key.
    //
    // The direct C# method calls below cover the per-call rejection path. The HTTP-layer
    // session test (assert /mcp accepts unauthenticated tools/list) lives in
    // McpTransportBindingTests.cs where the SDK stream transport is available.

    [Fact]
    public async Task DirectCall_NoAuthKeyAndNoHeader_ReturnsAuthError()
    {
        var (tools, scope) = CreateDiscoveryTools();
        using var _ = scope;

        var result = await tools.ListAppsAsync(status: null, authKey: null, CancellationToken.None);

        (result.IsError ?? false).ShouldBeTrue();

        var text = GetFirstText(result);

        text.ShouldContain("Authentication required");
    }

    [Fact]
    public async Task DirectCall_InvalidAuthKey_ReturnsAuthError()
    {
        var (tools, scope) = CreateDiscoveryTools();
        using var _ = scope;

        var result = await tools.ListAppsAsync(status: null, authKey: "01BADKEYNOTINDB000000000X", CancellationToken.None);

        (result.IsError ?? false).ShouldBeTrue();

        var text = GetFirstText(result);

        text.ShouldContain("Invalid or deactivated authKey");
    }

    // -------- Discovery: get_system_status uptime --------

    [Fact]
    public async Task GetSystemStatus_UptimeSeconds_IsNonNegative()
    {
        // Validates that IApplicationStartTime eliminates the 0 / -0 race. Card #222.
        var (tools, scope) = CreateDiscoveryTools();
        using var _ = scope;

        var result = await tools.GetSystemStatusAsync(authKey: ApiFixture.AdminKey, CancellationToken.None);

        (result.IsError ?? false).ShouldBeFalse();

        var text = GetFirstText(result);
        var json = JsonDocument.Parse(text);

        var uptimeSeconds = json.RootElement.GetProperty("uptimeSeconds").GetDouble();

        uptimeSeconds.ShouldBeGreaterThanOrEqualTo(0.0, "uptimeSeconds must not be negative");
    }

    [Fact]
    public async Task GetSystemStatus_UptimeSeconds_AgreesWithStatusEndpointWithinOneSec()
    {
        // MCP tool and REST endpoint share IApplicationStartTime, so their uptimeSeconds
        // values should agree within a tight tolerance (1s covers the round-trip delta).
        var (tools, scope) = CreateDiscoveryTools();
        using var _ = scope;

        var mcpResult = await tools.GetSystemStatusAsync(authKey: ApiFixture.AdminKey, CancellationToken.None);
        using var httpResponse = await _client.GetAsync(new Uri("/api/v1/status", UriKind.Relative));

        httpResponse.IsSuccessStatusCode.ShouldBeTrue();

        var mcpText = GetFirstText(mcpResult);
        var mcpJson = JsonDocument.Parse(mcpText);
        var mcpUptime = mcpJson.RootElement.GetProperty("uptimeSeconds").GetDouble();

        var httpJson = await httpResponse.Content.ReadFromJsonAsync<JsonElement>();
        var httpUptime = httpJson.GetProperty("uptimeSeconds").GetDouble();

        Math.Abs(httpUptime - mcpUptime).ShouldBeLessThan(1.0, "MCP and REST uptimeSeconds should agree within 1 second");
    }

    private static string GetFirstText(CallToolResult result) =>
        result.Content is { Count: > 0 } && result.Content[0] is TextContentBlock textBlock
            ? textBlock.Text
            : string.Empty;
}
