using System.IO.Pipelines;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Collabhost.Api.Registry;
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

// Card #365: integration coverage for the runtime-config-file writer triggers
// on the MCP surface. The pre-Card-#365 MCP path was structurally never wired
// to the writer -- the existing McpToolTests called tool classes as direct C#
// methods, which would have passed against the never-wired branches because
// the direct-method tests don't traverse the trigger seam.
//
// What these exercise
// -------------------
// 1. MCP `start_app` on a routing-only app renders the file. CATCHES Fix-B
//    (the writer call was structurally absent from the MCP routing-only
//    branch in LifecycleTools.StartAppAsync).
// 2. MCP `update_settings` with a runtime-config-file change re-renders the
//    file when the route is currently up. CATCHES Fix-C (the writer call was
//    structurally absent from ConfigurationTools.UpdateSettingsAsync).
//
// Fixture pattern mirrors McpTransportBindingTests (Card #331): a duplex pipe
// pair connects an in-process McpServer to an McpClient, both resolved from
// the production DI container so the test exercises the same WithTools<T>()
// registrations the production HTTP transport sees. HTTP/SSE transport is
// incompatible with TestHost's synchronous content buffering (see the existing
// McpToolTests comment), so the stream-transport shape is the lightest viable
// fixture for the binding-and-trigger seam.
// CA1001: owns _serverCts (IDisposable); cleaned up via IAsyncLifetime.DisposeAsync.
#pragma warning disable CA1001
[Collection("Api")]
public class RuntimeConfigFileTriggerTests(ApiFixture fixture) : IAsyncLifetime
#pragma warning restore CA1001
{
    private readonly ApiFixture _fixture = fixture;

    private McpServer? _server;
    private McpClient? _client;
    private Task? _serverRunTask;
    private CancellationTokenSource? _serverCts;
    private string _artifactDirectory = null!;

    public async ValueTask InitializeAsync()
    {
        _artifactDirectory = Path.Combine
        (
            Path.GetTempPath(),
            "collabhost-mcp-rcf-" + Guid.NewGuid().ToString("N")
        );

        Directory.CreateDirectory(_artifactDirectory);

        var options = _fixture.Services.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var scope = _fixture.Services.CreateAsyncScope();

        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

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
                // VSTHRD003: same async-context flow rationale as McpTransportBindingTests.
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

        if (Directory.Exists(_artifactDirectory))
        {
            try
            {
                Directory.Delete(_artifactDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public async Task StartApp_RoutingOnly_WithNonEmptyValues_RendersConfigFile()
    {
        // CATCHES Fix-B: the writer call was structurally absent from MCP
        // LifecycleTools.StartAppAsync's routing-only branch pre-Card-#365.
        var slug = await RegisterStaticSiteAsync
        (
            apiBaseUrl: "https://mcp-start.example.com/api/v1"
        );

        try
        {
            var result = await _client!.CallToolAsync
            (
                "start_app",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["slug"] = slug,
                    ["authKey"] = ApiFixture.AdminKey
                },
                cancellationToken: CancellationToken.None
            );

            (result.IsError ?? false).ShouldBeFalse
            (
                "MCP start_app on a routing-only app must succeed. Body: " + RenderContent(result)
            );

            var targetPath = ResolveConfigTargetPath(slug);

            File.Exists(targetPath).ShouldBeTrue
            (
                "MCP start_app (routing-only) MUST trigger the runtime-config-file writer. "
                + "Pre-Card-#365 the writer call was structurally absent from MCP "
                + "LifecycleTools.StartAppAsync -- this assertion fails against that shape."
            );

            var parsed = JsonNode.Parse(await File.ReadAllTextAsync(targetPath, CancellationToken.None))!.AsObject();
            parsed["apiBaseUrl"]!.GetValue<string>().ShouldBe("https://mcp-start.example.com/api/v1");
        }
        finally
        {
            await DeleteAppAsync(_fixture.Client, slug);
        }
    }

    [Fact]
    public async Task UpdateSettings_RuntimeConfigChange_RouteEnabled_RendersConfigFile()
    {
        // CATCHES Fix-C: the writer call was structurally absent from MCP
        // ConfigurationTools.UpdateSettingsAsync pre-Card-#365. This is the
        // exact path Theo's Test 1 exercised against collaboard.collabot.dev
        // v1.6.1 from his Ecosystem PM bot seat.
        var slug = await RegisterStaticSiteAsync
        (
            apiBaseUrl: "https://mcp-initial.example.com/api/v1"
        );

        try
        {
            // Bring the route up so update_settings's gate fires either pre-
            // or post-Card-#365 (we want this test to isolate Fix-C from
            // Fix-A's gate change). Start via REST so we don't double-test
            // start's MCP path.
            using var startRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/apps/{slug}/start");
            startRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
            var startResponse = await _fixture.Client.SendAsync(startRequest, CancellationToken.None);
            startResponse.IsSuccessStatusCode.ShouldBeTrue
            (
                "Test setup: REST start_app failed -- need the route up so the gate fires."
            );

            // Clear the prior write so we can detect a fresh MCP-driven render.
            var targetPath = ResolveConfigTargetPath(slug);
            File.Delete(targetPath);

            // Now exercise MCP update_settings with a runtime-config-file change.
            // The settings parameter is a JSON string -- kebab-case capability key.
            var settingsJson = new JsonObject
            {
                ["runtime-config-file"] = new JsonObject
                {
                    ["values"] = new JsonObject
                    {
                        ["apiBaseUrl"] = "https://mcp-updated.example.com/api/v1"
                    }
                }
            }.ToJsonString();

            var result = await _client!.CallToolAsync
            (
                "update_settings",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["slug"] = slug,
                    ["settings"] = settingsJson,
                    ["authKey"] = ApiFixture.AdminKey
                },
                cancellationToken: CancellationToken.None
            );

            (result.IsError ?? false).ShouldBeFalse
            (
                "MCP update_settings must succeed. Body: " + RenderContent(result)
            );

            File.Exists(targetPath).ShouldBeTrue
            (
                "MCP update_settings with a runtime-config-file change MUST trigger the "
                + "runtime-config-file writer when the route is currently up. Pre-Card-#365 "
                + "the writer call was structurally absent from MCP ConfigurationTools."
                + "UpdateSettingsAsync -- this is the path Theo's Test 1 exercised."
            );

            var parsed = JsonNode.Parse(await File.ReadAllTextAsync(targetPath, CancellationToken.None))!.AsObject();
            parsed["apiBaseUrl"]!.GetValue<string>().ShouldBe("https://mcp-updated.example.com/api/v1");
        }
        finally
        {
            await DeleteAppAsync(_fixture.Client, slug);
        }
    }

    // #369: the writer renders into the app's writable data dir, resolved via the
    // host's AppDataPathResolver -- the same path production writes to.
    private string ResolveConfigTargetPath(string slug)
    {
        var resolver = _fixture.Services.GetRequiredService<AppDataPathResolver>();

        return Path.Combine(resolver.ResolveFor(slug), "config.json");
    }

    private async Task<string> RegisterStaticSiteAsync(string apiBaseUrl)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"mcp-rcf-{suffix}";

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
        createRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        createRequest.Content = JsonContent.Create
        (
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["name"] = slug,
                ["displayName"] = "MCP RCF Trigger Test",
                ["appTypeSlug"] = "static-site",
                ["values"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["artifact"] = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["location"] = _artifactDirectory
                    },
                    ["runtime-config-file"] = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["values"] = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["apiBaseUrl"] = apiBaseUrl
                        }
                    }
                }
            }
        );

        var response = await _fixture.Client.SendAsync(createRequest, CancellationToken.None);
        response.IsSuccessStatusCode.ShouldBeTrue
        (
            $"Test setup: registration failed with {response.StatusCode}: "
            + await response.Content.ReadAsStringAsync(CancellationToken.None)
        );

        return slug;
    }

    private static async Task DeleteAppAsync(HttpClient client, string slug)
    {
        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/apps/{slug}");
        deleteRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        await client.SendAsync(deleteRequest, CancellationToken.None);
    }

    private static string RenderContent(CallToolResult result) =>
        result.Content is { Count: > 0 } && result.Content[0] is TextContentBlock block
            ? block.Text
            : "<no content>";
}
