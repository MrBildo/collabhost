using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Mcp;
using Collabhost.Api.Platform;
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
            sp.GetRequiredService<ReloadProxyOperation>(),
            sp.GetRequiredService<UpdateSettingsOperation>(),
            sp.GetRequiredService<McpRequestAuthenticator>()
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
            sp.GetRequiredService<CreateAppOperation>(),
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

    // -------- Configuration: reload_proxy (operation-spine adapter, #406 PR 4) --------
    //
    // reload_proxy now adapts the marker command, calls the injected ReloadProxyOperation (the
    // trivial app-less op: RequestSync + the actor-stamped proxy.reloaded event), and maps the
    // result back. This direct-call test is the MCP-observable oracle the migration must preserve:
    // the fixed "reload requested" success message, byte-identical to pre-migration. (RequestSync
    // only enqueues a channel write -- no Caddy contact -- so this runs without a live proxy.)

    [Fact]
    public async Task ReloadProxy_ReturnsFixedSuccessMessage()
    {
        var (tools, scope) = CreateConfigurationTools();
        using var _ = scope;

        var result = await tools.ReloadProxyAsync(authKey: ApiFixture.AdminKey, CancellationToken.None);

        (result.IsError ?? false).ShouldBeFalse();

        var text = GetFirstText(result);

        text.ShouldContain("reload requested");
    }

    // -------- Configuration: update_settings (operation-spine adapter, #406 PR 5 + parity-fix) --------
    //
    // update_settings parses its raw `settings` string into a JsonObject, adapts it into the
    // normalized UpdateSettingsCommand (MCP flags: ValidateMergedOverrides + RefreshProbesOnArtifact-
    // Change TRUE since the #406 settings parity-fix, RejectUnknownSection true), calls the injected
    // UpdateSettingsOperation, and maps the result back. The byte-preserved shapes (fixed success
    // message, unknown-section reject, app-not-found) are pinned below; the HSTS-collision reject is
    // the NEW parity behavior (the merged-validation flip -- pre-fix MCP silently accepted it).

    [Fact]
    public async Task UpdateSettings_ValidChange_ReturnsFixedSuccessMessage()
    {
        var slug = await CreateTestAppAsync();

        try
        {
            var (tools, scope) = CreateConfigurationTools();
            using var _ = scope;

            var result = await tools.UpdateSettingsAsync
            (
                slug,
                "{\"runtime-config-file\":{\"values\":{\"apiBaseUrl\":\"https://mcp.example/api\"}}}",
                authKey: ApiFixture.AdminKey,
                CancellationToken.None
            );

            (result.IsError ?? false).ShouldBeFalse();

            var text = GetFirstText(result);

            text.ShouldContain($"Settings updated for app '{slug}'");
        }
        finally
        {
            await DeleteTestAppAsync(slug);
        }
    }

    [Fact]
    public async Task UpdateSettings_UnknownSection_ReturnsError()
    {
        var slug = await CreateTestAppAsync();

        try
        {
            var (tools, scope) = CreateConfigurationTools();
            using var _ = scope;

            var result = await tools.UpdateSettingsAsync
            (
                slug,
                "{\"not-a-capability\":{\"foo\":\"bar\"}}",
                authKey: ApiFixture.AdminKey,
                CancellationToken.None
            );

            (result.IsError ?? false).ShouldBeTrue();

            var text = GetFirstText(result);

            text.ShouldContain("Unknown capability section 'not-a-capability'");
        }
        finally
        {
            await DeleteTestAppAsync(slug);
        }
    }

    [Fact]
    public async Task UpdateSettings_UnknownApp_ReturnsError()
    {
        var (tools, scope) = CreateConfigurationTools();
        using var _ = scope;

        var result = await tools.UpdateSettingsAsync
        (
            "no-such-app-xyz",
            "{\"runtime-config-file\":{\"values\":{\"apiBaseUrl\":\"https://x\"}}}",
            authKey: ApiFixture.AdminKey,
            CancellationToken.None
        );

        (result.IsError ?? false).ShouldBeTrue();

        var text = GetFirstText(result);

        text.ShouldContain("no-such-app-xyz");
    }

    // NEW behavior (#406 settings parity-fix -- the one sanctioned behavior change of the spine arc):
    // MCP update_settings now runs CapabilityResolver.ValidateMergedOverrides (ValidateMergedOverrides
    // flag flipped false -> true, matching REST). This rejects the two-step HSTS double-emission
    // collision -- save a Strict-Transport-Security entry in the headers map, then later turn on
    // enableHsts -- that neither in-flight ValidateEdits delta trips alone but the merged state does.
    //
    // PRE-FIX this silently SUCCEEDED on MCP (the pre-migration path never ran merged-validation),
    // producing a security-header double-emission REST had always rejected. This test asserts the new
    // parity: the second MCP edit now returns an error naming the collision. A static-site binds
    // security-headers, so CreateTestAppAsync is the vehicle; the in-flight first edit (headers only)
    // succeeds, the second edit (enableHsts only) is what the merged check now rejects.
    [Fact]
    public async Task UpdateSettings_TwoStepHstsCollision_NowRejected()
    {
        var slug = await CreateTestAppAsync();

        try
        {
            var (tools, scope) = CreateConfigurationTools();
            using var _ = scope;

            // Step 1: author a Strict-Transport-Security entry in the freeform headers map. The
            // in-flight delta carries no enableHsts, so ValidateEdits' cross-field check does not trip
            // -- this save succeeds (pre-fix and post-fix alike).
            var firstResult = await tools.UpdateSettingsAsync
            (
                slug,
                "{\"security-headers\":{\"headers\":{\"Strict-Transport-Security\":\"max-age=600\"}}}",
                authKey: ApiFixture.AdminKey,
                CancellationToken.None
            );

            (firstResult.IsError ?? false).ShouldBeFalse
            (
                "First edit (headers map only) must succeed -- the collision needs the merged state. "
                + "Body: " + GetFirstText(firstResult)
            );

            // Step 2: turn on enableHsts. The in-flight delta carries no headers map, so ValidateEdits
            // alone still passes -- only the post-merge ValidateMergedOverrides sees BOTH and rejects.
            var secondResult = await tools.UpdateSettingsAsync
            (
                slug,
                "{\"security-headers\":{\"enableHsts\":true}}",
                authKey: ApiFixture.AdminKey,
                CancellationToken.None
            );

            (secondResult.IsError ?? false).ShouldBeTrue
            (
                "MCP update_settings MUST now reject the merged HSTS double-emission collision "
                + "(ValidateMergedOverrides flag flipped true by the #406 parity-fix). Pre-fix the "
                + "MCP path ran no merged-validation and this silently succeeded. Body: "
                + GetFirstText(secondResult)
            );

            GetFirstText(secondResult).ShouldContain("Strict-Transport-Security");
        }
        finally
        {
            await DeleteTestAppAsync(slug);
        }
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

    // Card #406 PR 6, F-1 lock-test: the disclosed, deliberate parse-before-exists ordering on the MCP
    // surface. A doubly-invalid register_app -- an EXISTING slug AND malformed settings JSON -- now
    // surfaces the JSON-parse error (built at the adapter, before the operation runs), NOT the
    // exists-conflict (owned by CreateAppOperation, never reached on this path). Pre-migration the MCP
    // exists-check ran first and returned the conflict. The reorder is forced by the spine (the parse
    // builds the command the operation consumes) and is zero state-impact; this test pins which error
    // surfaces so the divergence can't silently flip back if someone "restores" a surface-level
    // exists-check. The directoryRequired gate passes (valid installDirectory) so the parse is reached.
    [Fact]
    public async Task RegisterApp_ExistingSlugAndMalformedSettings_ReturnsParseErrorNotExistsError()
    {
        var existingSlug = await CreateTestAppAsync();
        var installDirectory = Path.Combine(Path.GetTempPath(), $"collabhost-test-{Guid.NewGuid():N}");

        Directory.CreateDirectory(installDirectory);

        try
        {
            var (tools, scope) = CreateRegistrationTools();
            using var _ = scope;

            // name derives to the EXISTING slug (CreateTestAppAsync seeds name == slug, already
            // lowercase + hyphenated, so name.ToLowerInvariant().Replace(' ', '-') is identity).
            var result = await tools.RegisterAppAsync
            (
                existingSlug,
                "static-site",
                installDirectory,
                settings: "{bad json",
                authKey: ApiFixture.AdminKey,
                CancellationToken.None
            );

            (result.IsError ?? false).ShouldBeTrue();

            var text = GetFirstText(result);

            text.ShouldContain("Invalid JSON in settings parameter");
            text.ShouldNotContain("already exists");
        }
        finally
        {
            await DeleteTestAppAsync(existingSlug);

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
