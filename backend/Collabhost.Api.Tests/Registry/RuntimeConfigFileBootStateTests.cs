using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Collabhost.Api.Registry;
using Collabhost.Api.Tests.Fixtures;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Registry;

// Card #365: captures the precise boot-state interaction that allowed the
// runtime-config-file writer-trigger defect to ship -- and that the production
// case in collaboard.collabot.dev (Theo's Test 1) exercised.
//
// The defect mechanism
// --------------------
// ProxyManager._routeStates is an in-memory dict populated only by:
//   - EnableRoute(slug)  -> writes true
//   - DisableRoute(slug) -> writes false
//   - HydrateRouteStatesFromPersistenceAsync -> writes false ONLY for apps
//     whose StoppedByOperator == true
//   - EnableAutoStartRoutesAsync -> calls EnableRoute, but only for routing-
//     only apps whose auto-start.enabled == true (no built-in routing-only
//     type today carries auto-start.enabled=true).
//
// So after Collabhost restarts, a static-site whose route was up before the
// restart and has never been operator-stopped has NO entry in _routeStates.
// LoadRoutableAppsAsync uses IsRouteEnabled (default-true) so Caddy serves
// the route -- the operator sees the site working.
//
// Pre-Card-#365 the REST update_settings gate was IsRouteExplicitlyEnabled
// (default-false on absent entry). For exactly this production-common case
// the writer NEVER fired -- the operator's settings save persisted, but the
// on-disk config.json stayed stale forever. The bug was structurally invisible
// to unit tests because every prior writer test exercised the writer directly.
//
// What this exercises
// -------------------
// Create a static-site directly via AppStore.CreateAsync (StoppedByOperator =
// false, _routeStates absent for the slug). Apply a runtime-config-file
// settings change via REST PUT /api/v1/apps/{slug}/settings. Assert the file
// on disk got rendered. Pre-fix this fails because the gate is default-false.
// Post-fix it passes because the gate is default-true.
//
// This is the highest-value regression-prevention test of the three -- it
// captures the exact state machine that allowed the defect to ship.
[Collection("Api")]
public class RuntimeConfigFileBootStateTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly ApiFixture _fixture = fixture;

    private string _artifactDirectory = null!;

    public Task InitializeAsync()
    {
        _artifactDirectory = Path.Combine
        (
            Path.GetTempPath(),
            "collabhost-rcf-boot-" + Guid.NewGuid().ToString("N")
        );

        Directory.CreateDirectory(_artifactDirectory);

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
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

        return Task.CompletedTask;
    }

    [Fact]
    public async Task UpdateSettings_RouteStateAbsent_RendersConfigFile()
    {
        // Construct the absent-_routeStates-entry boot state directly via
        // AppStore.CreateAsync -- bypasses the REST CreateAppAsync path's
        // proxy.DisableRoute() call for routing-only-no-process apps that
        // would otherwise seed _routeStates[slug] = false.
        var appStore = _fixture.Services.GetRequiredService<AppStore>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"rcf-boot-{suffix}";

        var app = new App
        {
            Slug = slug,
            DisplayName = "RCF Boot State Test",
            AppTypeSlug = "static-site",
            StoppedByOperator = false
        };

        await appStore.CreateAsync(app, CancellationToken.None);

        try
        {
            // Seed the artifact location and an initial runtime-config-file values
            // entry via direct override saves. The REST settings endpoint is what
            // we want to exercise the BUG against -- not what we want to use to
            // set up the pre-state. Bypass the REST endpoint here for the same
            // reason we bypass it for the registration: REST settings on a
            // routing-only app would set _routeStates as a side effect we want
            // to keep absent.
            //
            // Path encoding via JsonObject keeps backslashes escaped correctly
            // on Windows artifact paths (the runtime-config-file capability is
            // a static-site concept and the test runs cross-platform).
            var artifactOverride = new JsonObject
            {
                ["location"] = _artifactDirectory
            };

            await appStore.SaveOverrideAsync
            (
                app.Id,
                "artifact",
                artifactOverride.ToJsonString(),
                CancellationToken.None
            );

            var initialValues = new JsonObject
            {
                ["apiBaseUrl"] = "https://initial.example.com/api/v1"
            };
            var initialRcf = new JsonObject
            {
                ["values"] = initialValues
            };

            await appStore.SaveOverrideAsync
            (
                app.Id,
                "runtime-config-file",
                initialRcf.ToJsonString(),
                CancellationToken.None
            );

            // Invalidate cached overrides + slug-keyed app so the next read sees
            // the fresh state.
            appStore.Invalidate(slug);
            appStore.InvalidateOverrides(app.Id);

            // Now exercise the defect: REST update_settings with a runtime-
            // config-file change, _routeStates absent for the slug.
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
                                ["apiBaseUrl"] = "https://rendered-by-update.example.com/api/v1"
                            }
                        }
                    }
                }
            );

            var updateResponse = await _fixture.Client.SendAsync(updateRequest);

            updateResponse.StatusCode.ShouldBe
            (
                HttpStatusCode.OK,
                "REST update_settings on a routing-only app with absent _routeStates "
                + "must succeed. Body: " + await updateResponse.Content.ReadAsStringAsync()
            );

            // #369: the writer renders into the app's writable data dir, resolved
            // via the host's AppDataPathResolver (the production path), not the
            // artifact dir.
            var dataPathResolver = _fixture.Services.GetRequiredService<AppDataPathResolver>();
            var targetPath = Path.Combine(dataPathResolver.ResolveFor(slug), "config.json");

            File.Exists(targetPath).ShouldBeTrue
            (
                "POST-CARD-#365: writer MUST fire when _routeStates has no entry "
                + "(default-true fallback in IsRouteEnabled). Pre-fix this assertion "
                + "FAILS because the gate was IsRouteExplicitlyEnabled (default-false), "
                + "exactly reproducing the production defect Theo's Test 1 surfaced "
                + "against collaboard.collabot.dev v1.6.1."
            );

            var parsed = JsonNode.Parse(await File.ReadAllTextAsync(targetPath))!.AsObject();
            parsed["apiBaseUrl"]!.GetValue<string>().ShouldBe("https://rendered-by-update.example.com/api/v1");
        }
        finally
        {
            // Clean up the app directly via AppStore (REST DELETE would also
            // touch the proxy, but we don't need that here).
            using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/apps/{slug}");
            deleteRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
            await _fixture.Client.SendAsync(deleteRequest);
        }
    }
}
