using System.IO.Pipelines;
using System.Net.Http.Json;

using Collabhost.Api.Probes;
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

// Card #366 + #406 settings parity-fix: integration coverage for the probe-trigger
// on two MCP surfaces (start_app and update_settings). The pre-Card-#366 MCP
// start_app path took no ProbeService dependency at all -- REST
// AppLifecycleEndpoints.StartAppAsync called RunProbesAsync on both start branches
// (routing-only and process-bearing); the MCP path called it on neither.
// Separately, the pre-#406-parity-fix MCP update_settings path never re-probed on
// an artifact-section change (REST did) -- the RefreshProbesOnArtifactChange flag
// was MCP-false. Either gap left probe-derived metadata (surfaced via get_app)
// stale after an MCP mutation.
//
// What this exercises
// -------------------
// (1) MCP `start_app` on a routing-only app (static-site) with a real artifact
// transitions the ProbeService cache from NeverProbed -> Fresh (Card #366).
// (2) MCP `update_settings` carrying an `artifact`-section change does the same
// (#406 parity-fix: the RefreshProbesOnArtifactChange flag flipped MCP-false ->
// true, matching REST). Both prove RunProbesAsync was actually invoked across the
// MCP transport boundary -- not merely that a call site exists.
//
// Why the cache, not a "method was called" mock: ProbeService is a singleton
// whose cache IS the operator-facing artifact (get_app reads GetCachedProbes).
// Asserting the cache state asserts the contract the card names -- the get_app
// metadata reflects the current artifact state after an MCP start.
//
// Fixture pattern mirrors RuntimeConfigFileTriggerTests / McpTransportBindingTests
// (Card #331): a duplex pipe pair connects an in-process McpServer to an
// McpClient, both resolved from the production DI container so the test
// exercises the same WithTools<T>() registrations the production HTTP transport
// sees. HTTP/SSE transport is incompatible with TestHost's synchronous content
// buffering, so the stream-transport shape is the lightest viable fixture for
// the binding-and-trigger seam.
// CA1001: owns _serverCts (IDisposable); cleaned up via IAsyncLifetime.DisposeAsync.
#pragma warning disable CA1001
[Collection("Api")]
public class ProbeTriggerTests(ApiFixture fixture) : IAsyncLifetime
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
            "collabhost-mcp-probe-" + Guid.NewGuid().ToString("N")
        );

        Directory.CreateDirectory(_artifactDirectory);

        // A real entry-point file so StaticSiteExtractor yields a non-empty
        // panel -- the probe result is then content-bearing, not just an
        // empty-but-fresh cache entry.
        await File.WriteAllTextAsync
        (
            Path.Combine(_artifactDirectory, "index.html"),
            "<!doctype html><title>probe-trigger-test</title>",
            CancellationToken.None
        );

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
    public async Task StartApp_RoutingOnly_RefreshesProbeCache()
    {
        // CATCHES Card #366: the RunProbesAsync call was structurally absent from
        // MCP LifecycleTools.StartAppAsync's routing-only branch (LifecycleTools
        // took no ProbeService dependency at all). This assertion fails against
        // that shape -- the cache stays NeverProbed.
        var (slug, appId) = await RegisterStaticSiteAsync();

        var probeService = _fixture.Services.GetRequiredService<ProbeService>();

        try
        {
            // Clean baseline: guarantee NeverProbed regardless of whether the
            // boot-time ProbeStartupService warm cycle touched this app id.
            probeService.InvalidateProbeCache(appId);

            probeService.GetCachedProbes(appId, "static-site").Status
                .ShouldBe
                (
                    ProbeCacheStatus.NeverProbed,
                    "Test setup: cache must be NeverProbed before the MCP start."
                );

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

            var cached = probeService.GetCachedProbes(appId, "static-site");

            cached.Status.ShouldBe
            (
                ProbeCacheStatus.Fresh,
                "MCP start_app (routing-only) MUST trigger ProbeService.RunProbesAsync. "
                + "Pre-Card-#366 LifecycleTools took no ProbeService dep -- the cache "
                + "stays NeverProbed against that shape."
            );

            cached.Entries.ShouldNotBeEmpty
            (
                "The static-site artifact has an index.html, so a refreshed probe "
                + "run must surface at least one probe entry."
            );
        }
        finally
        {
            await DeleteAppAsync(_fixture.Client, slug);
        }
    }

    [Fact]
    public async Task UpdateSettings_ArtifactChange_RefreshesProbeCache()
    {
        // NEW behavior (#406 settings parity-fix): MCP update_settings now re-probes
        // when the `artifact` section changes (RefreshProbesOnArtifactChange flag
        // flipped MCP-false -> true, matching REST). PRE-FIX the MCP path never
        // re-probed -- this assertion fails against that shape (the cache stays
        // NeverProbed after the MCP settings change).
        var (slug, appId) = await RegisterStaticSiteAsync();

        var probeService = _fixture.Services.GetRequiredService<ProbeService>();

        try
        {
            // Clean baseline: guarantee NeverProbed regardless of whether the
            // boot-time ProbeStartupService warm cycle (or the registration) touched
            // this app id.
            probeService.InvalidateProbeCache(appId);

            probeService.GetCachedProbes(appId, "static-site").Status
                .ShouldBe
                (
                    ProbeCacheStatus.NeverProbed,
                    "Test setup: cache must be NeverProbed before the MCP settings change."
                );

            // An artifact-section change. `location` is FieldEditableLocked (set at
            // registration only) -- editing it post-registration is rejected by
            // ValidateEdits -- so the editable `projectRoot` field carries the change.
            // The operation re-probes on the PRESENCE of an `artifact`-section change
            // (location OR project root). The value is a real directory so ValidateEdits
            // passes and the save reaches the probe-refresh gate.
            var escapedDirectory = _artifactDirectory.Replace("\\", "\\\\", StringComparison.Ordinal);
            var settingsJson = "{\"artifact\":{\"projectRoot\":\"" + escapedDirectory + "\"}}";

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
                "MCP update_settings with a valid artifact-location change must succeed. Body: "
                + RenderContent(result)
            );

            var cached = probeService.GetCachedProbes(appId, "static-site");

            cached.Status.ShouldBe
            (
                ProbeCacheStatus.Fresh,
                "MCP update_settings (artifact change) MUST trigger ProbeService.RunProbesAsync "
                + "(#406 parity-fix flipped RefreshProbesOnArtifactChange true). Pre-fix the MCP "
                + "path never re-probed -- the cache stays NeverProbed against that shape."
            );

            cached.Entries.ShouldNotBeEmpty
            (
                "The static-site artifact has an index.html, so a refreshed probe "
                + "run must surface at least one probe entry."
            );
        }
        finally
        {
            await DeleteAppAsync(_fixture.Client, slug);
        }
    }

    [Fact]
    public async Task DeleteApp_InvalidatesProbeCache()
    {
        // CATCHES the #406 spine PR 7 parity-fix: pre-migration MCP delete_app NEVER invalidated the
        // probe cache (RegistrationTools took no ProbeService dependency), where REST DeleteAppAsync
        // always did (Card #337). Unifying both surfaces onto DeleteAppOperation -- which calls
        // InvalidateProbeCache once -- closes the drift. This drives MCP delete_app across the real
        // stream transport and asserts the cache transitions Fresh -> NeverProbed. Against the
        // pre-fix shape (no InvalidateProbeCache reaching the MCP path) the entry would survive ->
        // RED. The op-level DeleteAppOperationTests proves the operation invalidates; this proves the
        // MCP surface reaches it -- the F-1 discipline (the op-level test is blind to which surface
        // calls the operation, so the behavior change gets a surface test).
        var (slug, appId) = await RegisterStaticSiteAsync();

        var probeService = _fixture.Services.GetRequiredService<ProbeService>();

        // Prime the cache to Fresh so the delete has a live entry to invalidate.
        await probeService.RunProbesAsync(appId, CancellationToken.None);

        probeService.GetCachedProbes(appId, "static-site").Status
            .ShouldBe
            (
                ProbeCacheStatus.Fresh,
                "Test setup: the cache must be Fresh before the MCP delete (the static-site has a "
                + "real artifact, so RunProbesAsync populates it)."
            );

        var result = await _client!.CallToolAsync
        (
            "delete_app",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["slug"] = slug,
                ["authKey"] = ApiFixture.AdminKey
            },
            cancellationToken: CancellationToken.None
        );

        (result.IsError ?? false).ShouldBeFalse
        (
            "MCP delete_app must succeed. Body: " + RenderContent(result)
        );

        probeService.GetCachedProbes(appId, "static-site").Status
            .ShouldBe
            (
                ProbeCacheStatus.NeverProbed,
                "MCP delete_app MUST invalidate the probe cache (#406 parity-fix: it now shares "
                + "DeleteAppOperation with REST, which always called InvalidateProbeCache). Pre-fix "
                + "the MCP path took no ProbeService dep -- the entry survives against that shape."
            );

        // No finally-delete: this test's whole point is that delete_app removed the app. A second
        // REST delete would 404 (harmless), but omitting it keeps the test's intent unambiguous.
    }

    private async Task<(string Slug, Ulid AppId)> RegisterStaticSiteAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"mcp-probe-{suffix}";

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
        createRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        createRequest.Content = JsonContent.Create
        (
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["name"] = slug,
                ["displayName"] = "MCP Probe Trigger Test",
                ["appTypeSlug"] = "static-site",
                ["values"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["artifact"] = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["location"] = _artifactDirectory
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

        var appStore = _fixture.Services.GetRequiredService<AppStore>();
        var app = await appStore.GetBySlugAsync(slug, CancellationToken.None);
        app.ShouldNotBeNull("Test setup: registered app must be retrievable by slug.");

        return (slug, app.Id);
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
