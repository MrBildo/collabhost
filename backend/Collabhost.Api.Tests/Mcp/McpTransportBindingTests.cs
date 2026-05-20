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

    public async Task InitializeAsync()
    {
        // Resolve the production-configured McpServerOptions. The DI container has already
        // populated ToolCollection from the WithTools<DiscoveryTools/LifecycleTools/...>
        // registrations in McpRegistration -- exactly the same tool set the real HTTP transport
        // sees.
        var options = _fixture.Services.GetRequiredService<IOptions<McpServerOptions>>().Value;

        // The production path sets a per-session ConfigureSessionOptions that resolves the
        // X-User-Key header and populates the scoped CurrentUser. We mirror that here by
        // resolving the admin user via AuthKeyResolver and seeding CurrentUser inside a scope
        // that the McpServer will hold throughout the test. McpServer.Create accepts an
        // IServiceProvider that tool factories use to resolve dependencies.
        //
        // Note: McpServerOptions.ScopeRequests defaults to true, so the SDK creates a fresh
        // scope per tools/call request. Our seeded CurrentUser is on a different scope, which
        // means activity-event recording inside the tool body will see an unresolved
        // CurrentUser and log a warning. Tool bodies catch that and continue -- the binding
        // assertion these tests target is unaffected. The warning is incidental log noise,
        // not a test failure.
        var resolver = _fixture.Services.GetRequiredService<AuthKeyResolver>();
        var user = await resolver.ResolveAsync(ApiFixture.AdminKey, CancellationToken.None);

        user.ShouldNotBeNull("Admin user must resolve from the integration AdminKey -- fixture invariant");

        var scope = _fixture.Services.CreateAsyncScope();
        var currentUser = scope.ServiceProvider.GetRequiredService<CurrentUser>();
        currentUser.Set(user);

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

    public async Task DisposeAsync()
    {
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
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["slug"] = slug },
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
            arguments: null,
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
            arguments: null,
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
                    ["installDirectory"] = installDirectory
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
            arguments: null,
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
