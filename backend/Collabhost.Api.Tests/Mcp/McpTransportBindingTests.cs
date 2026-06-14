using System.IO.Pipelines;
using System.Net.Http.Json;

using Collabhost.Api.Authorization;
using Collabhost.Api.Tests.Fixtures;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Mcp;

// Transport-level regression coverage for the MCP optional-parameter binding defect (Card #331).
//
// What this exercises
// -------------------
// The existing McpToolTests.cs calls the tool classes as direct C# methods and bypasses the
// Microsoft.Extensions.AI binding marshaller entirely. Those tests pass against the broken
// signatures today (no `= null` default on optional params) and would NOT have caught the bug
// the Ecosystem #124 operator hit -- a marshaller-thrown ArgumentException on a client call
// that omitted optional arguments, masked by ModelContextProtocol.Core to the generic
// "An error occurred invoking '<tool>'." string.
//
// These tests drive a real MCP `tools/call` through the SDK's stream transport (a duplex
// pipe pair connecting an in-process McpServer and McpClient), using the same McpServerOptions
// the production HTTP transport uses -- including the WithTools<T>() registrations resolved
// from the production DI container. If an MCP tool ships an optional parameter without a
// binding-visible C# default, a call that omits that argument here will return IsError=true
// with the generic masked error string, and the test fails.
//
// Fixture choice rationale
// ------------------------
// Per the existing McpToolTests.cs comment, the HTTP/SSE transport is incompatible with
// TestHost's synchronous content buffering, which is why direct-method calls were the prior
// approach. The MCP SDK ships StreamServerTransport / StreamClientTransport for exactly this
// case: a duplex stream pair carries JSON-RPC frames in-process, with the full server-side
// argument-binding pipeline intact (the layer where #331 lived). This is the lightest fixture
// that exercises the failing seam without standing up Kestrel or routing through HTTP/SSE.
// CA1001: owns _serverCts (IDisposable) but the IAsyncLifetime/DisposeAsync contract is the
// canonical async-cleanup shape across this test project. Same suppression rationale as
// ProxyManagerDegradedStateTests / ProxyAppSeederTests.
#pragma warning disable CA1001
[Collection("Api")]
public class McpTransportBindingTests(ApiFixture fixture) : IAsyncLifetime
#pragma warning restore CA1001
{
    private readonly ApiFixture _fixture = fixture;

    private McpServer? _server;
    private McpClient? _client;
    private Task? _serverRunTask;
    private CancellationTokenSource? _serverCts;

    public async ValueTask InitializeAsync()
    {
        // Resolve the production-configured McpServerOptions. The DI container has already
        // populated ToolCollection from the WithTools<DiscoveryTools/LifecycleTools/...>
        // registrations in McpRegistration -- exactly the same tool set the real HTTP transport
        // sees.
        var options = _fixture.Services.GetRequiredService<IOptions<McpServerOptions>>().Value;

        // Card #332: auth moved from session-time to per-call. Each tools/call invocation now
        // carries its own `authKey` argument; the tool body's McpRequestAuthenticator resolves
        // it and seeds CurrentUser inside the call's scope. The fixture no longer pre-seeds
        // CurrentUser -- the binding tests below pass ApiFixture.AdminKey via the per-call
        // arguments dictionary, exactly as a real MCP client would.
        var scope = _fixture.Services.CreateAsyncScope();

        // Duplex pipe pair. Each Pipe is a one-way FIFO: clientToServer carries client->server
        // frames, serverToClient carries server->client frames. Stream transports treat one end
        // of each as a Stream via PipeReader/PipeWriter adapters.
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        // Server reads from clientToServer (input) and writes to serverToClient (output).
        var serverTransport = new StreamServerTransport
        (
            inputStream: clientToServer.Reader.AsStream(),
            outputStream: serverToClient.Writer.AsStream()
        );

        _server = McpServer.Create
        (
            serverTransport,
            options,
            NullLoggerFactory.Instance,
            scope.ServiceProvider
        );

        _serverCts = new CancellationTokenSource();
        _serverRunTask = _server.RunAsync(_serverCts.Token);

        // Client writes to clientToServer (server's input) and reads from serverToClient
        // (server's output).
        var clientTransport = new StreamClientTransport
        (
            serverInput: clientToServer.Writer.AsStream(),
            serverOutput: serverToClient.Reader.AsStream()
        );

        _client = await McpClient.CreateAsync(clientTransport, cancellationToken: CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        if (_serverCts is not null)
        {
            await _serverCts.CancelAsync();
        }

        if (_serverRunTask is not null)
        {
            try
            {
                // VSTHRD003: awaiting a field-stored Task is intentional -- the server's
                // RunAsync was launched in InitializeAsync on the same async-context flow used
                // by the test methods, and we want the same context to observe its shutdown.
#pragma warning disable VSTHRD003
                await _serverRunTask;
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
        }

        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        _serverCts?.Dispose();
    }

    // The defect (Card #331): the MCP tool-binding marshaller treats parameters with no C#
    // default as REQUIRED, even when the type is nullable (`int?`, `string?`). A client call
    // that omits the optional argument throws ArgumentException upstream of the method body,
    // which ModelContextProtocol.Core masks to the generic error string below. Asserting on
    // both flags (no error AND no generic-masked string) keeps the test honest if the masking
    // shape changes.
    private const string _genericMaskedErrorPrefix = "An error occurred invoking";

    private static void ShouldNotBeBindingError(CallToolResult result, string toolName)
    {
        (result.IsError ?? false).ShouldBeFalse
        (
            $"tools/call '{toolName}' returned IsError=true; this is the #331 masked-binding-error shape "
            + "if the body never ran. Inspect Content for the actual reason."
        );

        if (result.Content is { Count: > 0 } && result.Content[0] is TextContentBlock block)
        {
            block.Text.StartsWith(_genericMaskedErrorPrefix, StringComparison.Ordinal).ShouldBeFalse
            (
                $"tools/call '{toolName}' returned the generic masked error string -- this is the #331 "
                + "binding-error symptom (marshaller threw before the tool body ran). Add an explicit "
                + "`= null` / `= default` to every optional parameter on this tool. Body: " + block.Text
            );
        }
    }

    private static async Task<string> CreateStaticSiteAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"mcp-bind-{suffix}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        request.Content = JsonContent.Create
        (
            new
            {
                name = slug,
                displayName = "MCP Binding Test App",
                appTypeSlug = "static-site"
            }
        );

        var response = await client.SendAsync(request);
        response.IsSuccessStatusCode.ShouldBeTrue
        (
            $"Test setup: creating a static-site app via REST failed with {response.StatusCode}."
        );

        return slug;
    }

    private static async Task DeleteAppAsync(HttpClient client, string slug)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/apps/{slug}");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        await client.SendAsync(request);
    }

    // ---- get_logs (LifecycleTools) -- the operator-reported symptom in Ecosystem #124 ----

    [Fact]
    public async Task GetLogs_OmittingOptionalLimitAndOffset_DoesNotReturnBindingError()
    {
        var client = _client!;
        var slug = await CreateStaticSiteAsync(_fixture.Client);

        try
        {
            var result = await client.CallToolAsync
            (
                "get_logs",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["slug"] = slug,
                    ["authKey"] = ApiFixture.AdminKey
                },
                cancellationToken: CancellationToken.None
            );

            ShouldNotBeBindingError(result, "get_logs");
        }
        finally
        {
            await DeleteAppAsync(_fixture.Client, slug);
        }
    }

    // ---- list_events (ActivityLogTools) -- four optional params, the densest defect site ----

    [Fact]
    public async Task ListEvents_OmittingAllOptionalParams_DoesNotReturnBindingError()
    {
        var client = _client!;

        var result = await client.CallToolAsync
        (
            "list_events",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["authKey"] = ApiFixture.AdminKey
            },
            cancellationToken: CancellationToken.None
        );

        ShouldNotBeBindingError(result, "list_events");
    }

    // ---- browse_filesystem (RegistrationTools) -- documented behavior is "omit path = roots" --

    [Fact]
    public async Task BrowseFilesystem_OmittingOptionalPath_DoesNotReturnBindingError()
    {
        var client = _client!;

        var result = await client.CallToolAsync
        (
            "browse_filesystem",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["authKey"] = ApiFixture.AdminKey
            },
            cancellationToken: CancellationToken.None
        );

        ShouldNotBeBindingError(result, "browse_filesystem");
    }

    // ---- register_app (RegistrationTools) -- the registration flow's optional settings arg ----

    [Fact]
    public async Task RegisterApp_OmittingOptionalSettings_DoesNotReturnBindingError()
    {
        var client = _client!;

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var appName = $"mcp-bind-reg-{suffix}";
        var installDirectory = Path.Combine(Path.GetTempPath(), $"collabhost-mcp-bind-{suffix}");

        Directory.CreateDirectory(installDirectory);

        try
        {
            var result = await client.CallToolAsync
            (
                "register_app",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["name"] = appName,
                    ["appTypeSlug"] = "executable",
                    ["installDirectory"] = installDirectory,
                    ["authKey"] = ApiFixture.AdminKey
                },
                cancellationToken: CancellationToken.None
            );

            ShouldNotBeBindingError(result, "register_app");
        }
        finally
        {
            await DeleteAppAsync(_fixture.Client, appName);

            if (Directory.Exists(installDirectory))
            {
                Directory.Delete(installDirectory, true);
            }
        }
    }

    // ---- list_apps (DiscoveryTools) -- the proof-of-mechanism control ----
    //
    // list_apps is the one tool that already shipped with `string? status = null`. It MUST stay
    // green here as the regression guard for the proof-of-mechanism asymmetry Kai's S53 recon
    // surfaced ("the one tool that wrote the default is the one that works"). If a future edit
    // strips the default off list_apps, this fails immediately.
    [Fact]
    public async Task ListApps_OmittingOptionalStatus_DoesNotReturnBindingError()
    {
        var client = _client!;

        var result = await client.CallToolAsync
        (
            "list_apps",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["authKey"] = ApiFixture.AdminKey
            },
            cancellationToken: CancellationToken.None
        );

        ShouldNotBeBindingError(result, "list_apps");
    }

    // ---- Class-level binding-contract assertion ----
    //
    // Surface-wide guard against future regressions. Every tool reflected from the running
    // McpServerOptions.ToolCollection must declare an explicit C# default on every optional
    // (nullable) parameter so ParameterInfo.HasDefaultValue is true. This is the same property
    // the per-tool calls above test individually. This assertion catches a new tool that ships
    // with the same shape before anyone files an operator-side bug.
    [Fact]
    public void EveryRegisteredTool_HasBindingVisibleDefaultsOnNullableParameters()
    {
        var options = _fixture.Services.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var tools = options.ToolCollection;

        tools.ShouldNotBeNull("McpServerOptions.ToolCollection must be populated by AddMcp()");
        tools.Count.ShouldBeGreaterThan(0, "At least one tool must be registered");

        var offenders = new List<string>();
        var resolvedMethodCount = 0;

        foreach (var tool in tools)
        {
            var method = ResolveBackingMethod(tool);

            if (method is null)
            {
                continue;
            }

            resolvedMethodCount++;

            foreach (var parameter in method.GetParameters())
            {
                // CancellationToken is bound via a dedicated marshaller branch in
                // Microsoft.Extensions.AI.Abstractions, not via the arguments dictionary --
                // skip it (a `= default` is harmless here but not required).
                if (parameter.ParameterType == typeof(CancellationToken))
                {
                    continue;
                }

                // A parameter is considered an offender if its type is nullable (Nullable T
                // value type or a reference type annotated nullable via NRT) AND it has no C#
                // default value (HasDefaultValue == false). Non-nullable parameters are
                // genuinely required and correctly bound as required by the marshaller.
                var isNullable = IsNullableType(parameter);

                if (isNullable && !parameter.HasDefaultValue)
                {
                    offenders.Add
                    (
                        $"{tool.ProtocolTool.Name} ({method.DeclaringType?.Name}.{method.Name}) "
                        + $"parameter '{parameter.Name}' is nullable ({parameter.ParameterType.Name}) "
                        + "but has no C# default. The MCP binding marshaller will treat it as REQUIRED "
                        + "and a client call that omits the argument will fail with a masked "
                        + "ArgumentException. Add '= null' (or '= default')."
                    );
                }
            }
        }

        // Fail loudly if reflection couldn't resolve ANY tool's backing method. A "0 of N"
        // resolution count means the MCP SDK changed shape and this assertion silently
        // bypasses the actual check -- the worst kind of green-but-useless test.
        resolvedMethodCount.ShouldBeGreaterThan
        (
            0,
            $"Reflection failed to resolve the backing MethodInfo for ANY of the {tools.Count} "
            + "registered tools. The MCP SDK's internal tool shape likely changed. Update "
            + "ResolveBackingMethod to track the new shape -- a silently-passing assertion is "
            + "useless. See AIFunctionMcpServerTool / AIFunction.UnderlyingMethod in "
            + "ModelContextProtocol.Core and Microsoft.Extensions.AI.Abstractions."
        );

        offenders.ShouldBeEmpty
        (
            "Found MCP tool parameters that will trigger the #331 binding defect:\n"
            + string.Join('\n', offenders)
        );
    }

    // ---- Per-call per-bot attribution coverage (Card #332) ----
    //
    // The decided model: per-call `authKey` is the only channel through which per-bot identity
    // enters a shared user-scope MCP server. These tests prove it end-to-end through real
    // tools/call invocations on the stream transport:
    //
    //   1. Two distinct callers, two distinct keys, in the same transport-bound test session.
    //   2. Each call's resulting activity-log row carries the calling user's ActorId/ActorName,
    //      NOT the other user's, NOT a shared identity.
    //
    // This is the load-bearing assertion the card asked for: "two distinct keys -> two distinct
    // CurrentUsers -> activity-log actors reflect the caller's identity, not a shared one."
    // The pre-#332 model could not satisfy this through a shared user-scope server because the
    // static header yielded one identity for every caller.

    [Fact]
    public async Task TwoCallers_TwoKeys_ActivityLogStampsEachCallersIdentity()
    {
        var client = _client!;

        // Create a second user (Agent role) directly via UserStore. The fixture's admin key
        // identifies user-A; we create user-B here so we have two distinct identities to drive
        // distinct tools/call invocations against the same transport-bound MCP server.
        var userStore = _fixture.Services.GetRequiredService<UserStore>();
        var userB = await userStore.CreateAsync($"Bot-B-{Guid.NewGuid():N}", UserRole.Agent, CancellationToken.None);

        // Resolve user-A via the admin key for ActorName comparison after the test calls.
        var resolver = _fixture.Services.GetRequiredService<AuthKeyResolver>();
        var userA = await resolver.ResolveAsync(ApiFixture.AdminKey, CancellationToken.None);
        userA.ShouldNotBeNull("Admin user must resolve from the integration AdminKey");

        // Each caller registers a static-site app via MCP -- a side-effecting call that records
        // an activity event (ActivityEventTypes.AppCreated) with the caller's ActorId/Name. The
        // app_created path is the simplest fixture-friendly side-effect (no process spawn, no
        // proxy state change, no transport quirks).
        var slugA = $"mcp-attr-a-{Guid.NewGuid().ToString("N")[..8]}";
        var slugB = $"mcp-attr-b-{Guid.NewGuid().ToString("N")[..8]}";
        var installA = Path.Combine(Path.GetTempPath(), $"collabhost-attr-a-{Guid.NewGuid():N}");
        var installB = Path.Combine(Path.GetTempPath(), $"collabhost-attr-b-{Guid.NewGuid():N}");

        Directory.CreateDirectory(installA);
        Directory.CreateDirectory(installB);

        try
        {
            var resultA = await client.CallToolAsync
            (
                "register_app",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["name"] = slugA,
                    ["appTypeSlug"] = "static-site",
                    ["installDirectory"] = installA,
                    ["authKey"] = ApiFixture.AdminKey
                },
                cancellationToken: CancellationToken.None
            );

            (resultA.IsError ?? false).ShouldBeFalse("register_app for user-A must succeed");

            var resultB = await client.CallToolAsync
            (
                "register_app",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["name"] = slugB,
                    ["appTypeSlug"] = "static-site",
                    ["installDirectory"] = installB,
                    ["authKey"] = userB.AuthKey
                },
                cancellationToken: CancellationToken.None
            );

            (resultB.IsError ?? false).ShouldBeFalse("register_app for user-B must succeed");

            // Query the activity log via REST -- the same log the dashboard reads from. The two
            // app_created events should carry the two distinct ActorIds (not a shared one).
            var (actorIdA, actorNameA) = await FindAppCreatedEventAsync(slugA);
            var (actorIdB, actorNameB) = await FindAppCreatedEventAsync(slugB);

            actorIdA.ShouldBe(userA.Id.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                "user-A's call should be attributed to user-A's id");
            actorNameA.ShouldBe(userA.Name);

            actorIdB.ShouldBe(userB.Id.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                "user-B's call should be attributed to user-B's id");
            actorNameB.ShouldBe(userB.Name);

            actorIdA.ShouldNotBe(actorIdB,
                "The two callers used distinct authKeys -- their activity-log actors must be distinct. "
                + "If both rows carry the same ActorId, per-call authKey is NOT being honored and "
                + "Card #332's load-bearing property is broken.");
        }
        finally
        {
            await DeleteAppAsync(_fixture.Client, slugA);
            await DeleteAppAsync(_fixture.Client, slugB);

            if (Directory.Exists(installA))
            {
                Directory.Delete(installA, true);
            }

            if (Directory.Exists(installB))
            {
                Directory.Delete(installB, true);
            }
        }
    }

    [Fact]
    public async Task PerCallAuthKey_OverridesHeaderFallback()
    {
        // Two co-existing channels: the X-User-Key header (captured at session setup as a
        // backward-compat fallback) and the per-call authKey argument. When both are set,
        // per-call wins -- this is the contract that lets one shared user-scope MCP server
        // (header pinned to one identity, or unset) deliver per-bot attribution through the
        // per-call argument.
        //
        // The transport-binding fixture does NOT set the header at session setup, so this test
        // verifies the precedence indirectly: a per-call authKey to a distinct user must yield
        // that user's identity in the activity log, regardless of what (if anything) the
        // session captured. The point is that the per-call value is the load-bearing one.
        var client = _client!;

        var userStore = _fixture.Services.GetRequiredService<UserStore>();
        var userC = await userStore.CreateAsync($"Bot-C-{Guid.NewGuid():N}", UserRole.Agent, CancellationToken.None);

        var slug = $"mcp-attr-override-{Guid.NewGuid().ToString("N")[..8]}";
        var install = Path.Combine(Path.GetTempPath(), $"collabhost-attr-override-{Guid.NewGuid():N}");
        Directory.CreateDirectory(install);

        try
        {
            var result = await client.CallToolAsync
            (
                "register_app",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["name"] = slug,
                    ["appTypeSlug"] = "static-site",
                    ["installDirectory"] = install,
                    ["authKey"] = userC.AuthKey
                },
                cancellationToken: CancellationToken.None
            );

            (result.IsError ?? false).ShouldBeFalse("per-call authKey path must succeed");

            var (actorId, actorName) = await FindAppCreatedEventAsync(slug);

            actorId.ShouldBe(userC.Id.ToString(null, System.Globalization.CultureInfo.InvariantCulture));
            actorName.ShouldBe(userC.Name);
        }
        finally
        {
            await DeleteAppAsync(_fixture.Client, slug);

            if (Directory.Exists(install))
            {
                Directory.Delete(install, true);
            }
        }
    }

    [Fact]
    public async Task MissingAuthKeyAndNoHeader_ReturnsAuthError()
    {
        // No per-call authKey, no X-User-Key header at session setup (the transport fixture
        // doesn't simulate one). The body should never run; the authenticator returns an
        // error CallToolResult instead. This is the contract that replaced the pre-#332
        // HTTP-401-at-session-setup behavior.
        var client = _client!;

        var result = await client.CallToolAsync
        (
            "list_apps",
            arguments: null,
            cancellationToken: CancellationToken.None
        );

        (result.IsError ?? false).ShouldBeTrue
        (
            "Missing authKey with no header fallback must yield an authentication error "
            + "from McpRequestAuthenticator, not a body that runs against an unresolved user."
        );

        if (result.Content is { Count: > 0 } && result.Content[0] is TextContentBlock block)
        {
            block.Text.ShouldContain("Authentication required");
        }
    }

    [Fact]
    public async Task DeleteApp_AgentRoleAuthKey_ReturnsForbidden()
    {
        // Entitlements.CanAccessTool excludes Agents from delete_app. The pre-#332 path
        // enforced this by removing delete_app from a non-admin's visible tool list at
        // session setup; per-call auth enforces it at invocation. Either way, the contract
        // is: an Agent attempting delete_app gets rejected, never executes.
        var client = _client!;

        var userStore = _fixture.Services.GetRequiredService<UserStore>();
        var agent = await userStore.CreateAsync($"Bot-Agent-{Guid.NewGuid():N}", UserRole.Agent, CancellationToken.None);

        // Create an app so the target exists -- the rejection must come from the auth
        // entitlement check, not from "app not found" deeper in the body.
        var slug = await CreateStaticSiteAsync(_fixture.Client);

        try
        {
            var result = await client.CallToolAsync
            (
                "delete_app",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["slug"] = slug,
                    ["authKey"] = agent.AuthKey
                },
                cancellationToken: CancellationToken.None
            );

            (result.IsError ?? false).ShouldBeTrue("delete_app with Agent authKey must be rejected");

            if (result.Content is { Count: > 0 } && result.Content[0] is TextContentBlock block)
            {
                var isEntitlementDenial = block.Text.Contains("Tool 'delete_app' is not available", StringComparison.Ordinal);
                var isAdminCheckDenial = block.Text.Contains("delete_app requires an administrator", StringComparison.Ordinal);

                (isEntitlementDenial || isAdminCheckDenial).ShouldBeTrue
                (
                    "Expected either the authenticator's entitlement-denial message or the "
                    + "body's belt-and-suspenders IsAdministrator check message. Got: " + block.Text
                );
            }
        }
        finally
        {
            // Static site still exists because delete_app was rejected; use the REST client
            // (admin key) to clean up.
            await DeleteAppAsync(_fixture.Client, slug);
        }
    }

    // Looks up the most recent ActivityEventTypes.AppCreated row for the given app slug via
    // the REST events endpoint. Returns the actor's id/name for attribution assertions.
    private async Task<(string ActorId, string ActorName)> FindAppCreatedEventAsync(string appSlug)
    {
        using var request = new HttpRequestMessage
        (
            HttpMethod.Get,
            $"/api/v1/events?appSlug={Uri.EscapeDataString(appSlug)}&eventType=app.created&limit=10"
        );
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var ct = _serverCts?.Token ?? CancellationToken.None;
        var response = await _fixture.Client.SendAsync(request, ct);
        response.IsSuccessStatusCode.ShouldBeTrue
        (
            $"Activity-log query for app {appSlug} returned {response.StatusCode}."
        );

        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);
        var items = body.GetProperty("items");

        items.GetArrayLength().ShouldBeGreaterThan
        (
            0,
            $"No app.created event recorded for {appSlug} -- the MCP register_app body never "
            + "wrote to the activity log. Per-call auth may not have populated CurrentUser before "
            + "the body ran."
        );

        var first = items[0];

        return
        (
            ActorId: first.GetProperty("actorId").GetString() ?? string.Empty,
            ActorName: first.GetProperty("actorName").GetString() ?? string.Empty
        );
    }

    private static System.Reflection.MethodInfo? ResolveBackingMethod(McpServerTool tool)
    {
        // MCP 1.2.0's AIFunctionMcpServerTool stores the wrapped Microsoft.Extensions.AI
        // AIFunction in an `AIFunction` property (instance, public-or-nonpublic depending on
        // SDK build). The AIFunction's public `UnderlyingMethod` property exposes the MethodInfo
        // that the marshaller uses for parameter binding -- the exact surface where #331 lives.
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic;

        var aiFunctionProp = tool.GetType().GetProperty("AIFunction", flags);

        if (aiFunctionProp?.GetValue(tool) is not { } aiFunction)
        {
            return null;
        }

        var underlyingMethodProp = aiFunction.GetType().GetProperty("UnderlyingMethod", flags);

        return underlyingMethodProp?.GetValue(aiFunction) as System.Reflection.MethodInfo;
    }

    private static bool IsNullableType(System.Reflection.ParameterInfo parameter)
    {
        // Nullable<T> value types (int?, bool?, etc.)
        if (Nullable.GetUnderlyingType(parameter.ParameterType) is not null)
        {
            return true;
        }

        // Nullable reference types -- inspect [NullableAttribute] / [NullableContextAttribute].
        // 1 = NotAnnotated, 2 = Annotated (nullable). The exact reading needs the field-level
        // NullableAttribute first, then the type's NullableContextAttribute as fallback.
        if (!parameter.ParameterType.IsValueType)
        {
            var nullableAttr = parameter.GetCustomAttributes(inherit: false)
                .OfType<Attribute>()
                .FirstOrDefault(a => string.Equals(a.GetType().FullName, "System.Runtime.CompilerServices.NullableAttribute", StringComparison.Ordinal));

            if (nullableAttr is not null)
            {
                var flagsField = nullableAttr.GetType().GetField("NullableFlags");

                if (flagsField?.GetValue(nullableAttr) is byte[] flags && flags.Length > 0)
                {
                    return flags[0] == 2;
                }
            }

            // Fall back to the declaring type's NullableContextAttribute.
            var contextAttr = parameter.Member.DeclaringType?.GetCustomAttributes(inherit: false)
                .OfType<Attribute>()
                .FirstOrDefault(a => string.Equals(a.GetType().FullName, "System.Runtime.CompilerServices.NullableContextAttribute", StringComparison.Ordinal));

            if (contextAttr is not null)
            {
                var flagField = contextAttr.GetType().GetField("Flag");

                if (flagField?.GetValue(contextAttr) is byte flag)
                {
                    return flag == 2;
                }
            }
        }

        return false;
    }
}
