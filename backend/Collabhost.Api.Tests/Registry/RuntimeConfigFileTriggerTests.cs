using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Collabhost.Api.Registry;
using Collabhost.Api.Tests.Fixtures;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Registry;

// Card #365: integration coverage for the runtime-config-file writer triggers on
// the REST surface. The defect was structurally invisible to the existing
// RuntimeConfigFileWriterTests because those exercise the writer directly --
// they would have passed against the broken trigger gate.
//
// What these exercise
// -------------------
// 1. REST POST /api/v1/apps/{slug}/start (routing-only path) renders the file.
//    Regression guard for the long-shipped trigger; would have caught a future
//    parity-strip on the REST start path.
// 2. REST PUT /api/v1/apps/{slug}/settings re-renders when the route was just
//    explicitly enabled via start_app. Confirms the gate fires on the
//    explicit-enabled path post-Card-#365.
// 3. REST PUT /api/v1/apps/{slug}/settings does NOT re-render when the route
//    is explicitly disabled (operator-stopped). Belt-and-suspenders -- the
//    widened gate must not accidentally fire on stopped apps.
//
// The boot-state interaction -- where _routeStates has NO entry for the slug
// because Collabhost just restarted and the app wasn't operator-stopped (the
// production-common case that allowed the defect to ship) -- is covered in
// RuntimeConfigFileBootStateTests, which can construct the absent-entry state
// directly via AppStore.CreateAsync (bypassing REST registration's DisableRoute
// write for routing-only-no-process apps).
//
// Fixture pattern
// ---------------
// The ApiFixture's WebApplicationFactory<Program> is the integration host; we
// register a static-site with an artifact-location pointing at a temp dir then
// drive the HTTP endpoints through HttpClient. The writer's on-disk output is
// the assertion -- we read the rendered config.json back from the WRITABLE DATA
// dir (#369), resolved via the host's own AppDataPathResolver (the same path
// production writes to), NOT the artifact dir.
[Collection("Api")]
public class RuntimeConfigFileTriggerTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly ApiFixture _fixture = fixture;

    private string _artifactDirectory = null!;

    public ValueTask InitializeAsync()
    {
        _artifactDirectory = Path.Combine
        (
            Path.GetTempPath(),
            "collabhost-rcf-trigger-" + Guid.NewGuid().ToString("N")
        );

        Directory.CreateDirectory(_artifactDirectory);

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        if (Directory.Exists(_artifactDirectory))
        {
            try
            {
                Directory.Delete(_artifactDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; tests should not fail on temp-dir teardown.
            }
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task StartApp_RoutingOnly_WithNonEmptyValues_RendersConfigFile()
    {
        var slug = await RegisterStaticSiteWithValuesAsync
        (
            apiBaseUrl: "https://start-app.example.com/api/v1"
        );

        try
        {
            using var startRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/apps/{slug}/start");
            startRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var startResponse = await _fixture.Client.SendAsync(startRequest);

            startResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            var targetPath = ResolveConfigTargetPath(slug);

            File.Exists(targetPath).ShouldBeTrue
            (
                "REST start (routing-only) must trigger the runtime-config-file writer."
            );

            var parsed = JsonNode.Parse(await File.ReadAllTextAsync(targetPath))!.AsObject();
            parsed["apiBaseUrl"]!.GetValue<string>().ShouldBe("https://start-app.example.com/api/v1");
        }
        finally
        {
            await DeleteAppAsync(slug);
        }
    }

    [Fact]
    public async Task UpdateSettings_RuntimeConfigChange_RouteExplicitlyEnabled_RendersConfigFile()
    {
        // Sanity coverage for the explicit-enabled path: registration + start +
        // update_settings must re-render. Both the pre-#365 (IsRouteExplicitly-
        // Enabled, default-false) and post-#365 (IsRouteEnabled, default-true)
        // gates pass here -- _routeStates[slug] = true after start_app. The
        // production-common case where _routeStates is ABSENT (and only the
        // post-#365 gate fires) lives in RuntimeConfigFileBootStateTests.
        var slug = await RegisterStaticSiteWithValuesAsync
        (
            apiBaseUrl: "https://initial.example.com/api/v1"
        );

        try
        {
            // Bring the route up explicitly so _routeStates[slug] = true.
            using var startRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/apps/{slug}/start");
            startRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var startResponse = await _fixture.Client.SendAsync(startRequest);
            startResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            // Now edit the runtime-config-file values. Kebab-case capability key,
            // so use a nested Dictionary literal rather than an anonymous record
            // (the latter would camelCase the key and miss the routing).
            using var updateRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/apps/{slug}/settings");
            updateRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
            updateRequest.Content = JsonContent.Create
            (
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["changes"] = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["runtime-config-file"] = new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["values"] = new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["apiBaseUrl"] = "https://updated.example.com/api/v1"
                            }
                        }
                    }
                }
            );

            var updateResponse = await _fixture.Client.SendAsync(updateRequest);

            updateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            var targetPath = ResolveConfigTargetPath(slug);
            var parsed = JsonNode.Parse(await File.ReadAllTextAsync(targetPath))!.AsObject();

            parsed["apiBaseUrl"]!.GetValue<string>().ShouldBe
            (
                "https://updated.example.com/api/v1",
                "REST update_settings with runtime-config-file changes must re-render "
                + "the file on disk when the route is currently up. Pre-Card-#365 this "
                + "would fail because the gate used IsRouteExplicitlyEnabled."
            );
        }
        finally
        {
            await DeleteAppAsync(slug);
        }
    }

    [Fact]
    public async Task UpdateSettings_RuntimeConfigChange_RouteExplicitlyDisabled_DoesNotRender()
    {
        // Belt-and-suspenders: the widened gate (Card #365 Fix-A) must not
        // accidentally fire on operator-stopped apps. Operator stops the route
        // -> _routeStates[slug] = false -> IsRouteEnabled returns false ->
        // writer must NOT fire on a subsequent update_settings.
        var slug = await RegisterStaticSiteWithValuesAsync
        (
            apiBaseUrl: "https://initial.example.com/api/v1"
        );

        try
        {
            // Start then stop so _routeStates[slug] = false explicitly.
            using var startRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/apps/{slug}/start");
            startRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
            await _fixture.Client.SendAsync(startRequest);

            using var stopRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/apps/{slug}/stop");
            stopRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
            await _fixture.Client.SendAsync(stopRequest);

            // Confirm the start wrote the file (sanity for the test's pre-state).
            var targetPath = ResolveConfigTargetPath(slug);
            File.Exists(targetPath).ShouldBeTrue("Pre-state: start_app should have rendered the file");

            // Delete the prior write so we can detect a re-render (which must not happen).
            File.Delete(targetPath);

            using var updateRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/apps/{slug}/settings");
            updateRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
            updateRequest.Content = JsonContent.Create
            (
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["changes"] = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["runtime-config-file"] = new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["values"] = new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["apiBaseUrl"] = "https://should-not-render.example.com/api/v1"
                            }
                        }
                    }
                }
            );

            var updateResponse = await _fixture.Client.SendAsync(updateRequest);
            updateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            File.Exists(targetPath).ShouldBeFalse
            (
                "Writer must NOT render when the route is explicitly disabled. "
                + "The settings save persists; the file on disk stays untouched."
            );
        }
        finally
        {
            await DeleteAppAsync(slug);
        }
    }

    // #369: the writer renders into the app's writable data dir, not the
    // artifact dir. Resolve the target via the host's own AppDataPathResolver
    // so the assertion tracks the production path.
    private string ResolveConfigTargetPath(string slug)
    {
        var resolver = _fixture.Services.GetRequiredService<AppDataPathResolver>();

        return Path.Combine(resolver.ResolveFor(slug), "config.json");
    }

    private async Task<string> RegisterStaticSiteWithValuesAsync(string apiBaseUrl)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"rcf-trig-{suffix}";

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
        createRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        createRequest.Content = JsonContent.Create
        (
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["name"] = slug,
                ["displayName"] = "RCF Trigger Test",
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

        var createResponse = await _fixture.Client.SendAsync(createRequest);
        createResponse.StatusCode.ShouldBe
        (
            HttpStatusCode.Created,
            $"Test setup: registering a static-site failed with {createResponse.StatusCode}: "
            + await createResponse.Content.ReadAsStringAsync()
        );

        return slug;
    }

    private async Task DeleteAppAsync(string slug)
    {
        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/apps/{slug}");
        deleteRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        await _fixture.Client.SendAsync(deleteRequest);
    }
}
