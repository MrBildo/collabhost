using Collabhost.Api.Capabilities;
using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;

namespace Collabhost.Api.StaticSite;

// Boot-time soft preflight that catches the runtime-config-overlay orphan: an
// upgrade into the writable-overlay fix can leave a static-site whose overlay
// route is active and whose runtime-config values are registered, but with no
// overlay file on disk -- the served config path then 404s and the SPA breaks
// until the operator re-applies the values. This footgun briefly took the
// Collaboard portal login down during the live v1.6.2 -> v1.6.3 upgrade.
//
// The operator-facing remedy (the post-upgrade re-import step) is documented in
// the upgrade runbook and INSTALL.md. This check is the safety net so a missed
// re-import is loud at boot instead of silent until the next portal load.
//
// Posture mirrors PortalReachabilityCheck / ListenPortValidator: warn, never
// halt. We do NOT auto-import -- that would silently take over an operator-
// maintained surface, the exact posture the explicit-operator-import design
// rejects. The warning names the affected app and the remedy; the operator acts.
//
// Detection property -- the warning fires for an app IFF all three hold:
//   1. the overlay route is active (the runtime-config-file overlay would be
//      emitted into the live Caddy config), AND
//   2. registered runtime-config values exist (resolved Values is non-empty,
//      i.e. the writer would render a file), AND
//   3. the overlay file is absent from the writable data dir.
// It must NOT fire on the legitimate states: no values registered (writer no-ops
// by design), file present (already rendered), or route inactive (operator
// stopped the route; nothing is being served to 404).
public static class RuntimeConfigOverlayOrphanCheck
{
    // Pure predicate -- the exact "fires iff all three" condition. Separated from
    // the fact-gathering so the four-state truth table is unit-testable without
    // a database or filesystem.
    public static bool IsOrphaned(bool routeActive, bool valuesRegistered, bool fileExists) =>
        routeActive && valuesRegistered && !fileExists;

    // Gathers the three facts per app and applies IsOrphaned. Takes its
    // collaborators explicitly (no DI registration) so the runtime caller in
    // Program.cs resolves the singletons and integration tests drive it against
    // the real AppStore / CapabilityStore / ProxyManager / AppDataPathResolver.
    //
    // Route-active fact mirrors LoadRoutableAppsAsync's overlay-emission gate:
    // the type binds routing + runtime-config-file, serve mode is FileServer with
    // a configured artifact dir, the config path is non-blank, and the route is
    // enabled (ProxyManager.IsRouteEnabled -- default-true post-hydration, false
    // only for operator-stopped routes). When any of those is false the overlay
    // subroute is not in the live config, so there is nothing to 404.
    public static async Task<RuntimeConfigOverlayOrphanOutcome> ValidateAsync
    (
        AppStore appStore,
        CapabilityStore capabilityStore,
        TypeStore typeStore,
        ProxyManager proxyManager,
        AppDataPathResolver dataPathResolver,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(appStore);
        ArgumentNullException.ThrowIfNull(capabilityStore);
        ArgumentNullException.ThrowIfNull(typeStore);
        ArgumentNullException.ThrowIfNull(proxyManager);
        ArgumentNullException.ThrowIfNull(dataPathResolver);

        var apps = await appStore.ListAsync(ct);

        var orphans = new List<RuntimeConfigOverlayOrphan>();

        foreach (var app in apps)
        {
            if (!typeStore.HasBinding(app.AppTypeSlug, "routing")
                || !typeStore.HasBinding(app.AppTypeSlug, "runtime-config-file"))
            {
                continue;
            }

            var routing = await capabilityStore.ResolveAsync<RoutingConfiguration>
            (
                "routing", app, ct
            );

            if (routing is null || routing.ServeMode != ServeMode.FileServer)
            {
                continue;
            }

            // No artifact dir -> LoadRoutableAppsAsync skips the route entirely,
            // so no overlay is emitted -- not an orphan.
            var artifact = await capabilityStore.ResolveAsync<ArtifactConfiguration>
            (
                "artifact", app, ct
            );

            if (artifact is null || string.IsNullOrWhiteSpace(artifact.Location))
            {
                continue;
            }

            var runtimeConfig = await capabilityStore.ResolveAsync<RuntimeConfigFileConfiguration>
            (
                "runtime-config-file", app, ct
            );

            // No config path -> the builder emits no overlay subroute.
            if (runtimeConfig is null || string.IsNullOrWhiteSpace(runtimeConfig.Path))
            {
                continue;
            }

            var routeActive = proxyManager.IsRouteEnabled(app.Slug);
            var valuesRegistered = runtimeConfig.Values.Count > 0;

            // The writable-dir target RenderAsync writes to, computed the same way
            // (resolve writable root, strip a single leading separator, combine).
            var expectedFilePath = ResolveExpectedFilePath
            (
                dataPathResolver.ResolveFor(app.Slug),
                runtimeConfig.Path
            );

            var fileExists = File.Exists(expectedFilePath);

            if (IsOrphaned(routeActive, valuesRegistered, fileExists))
            {
                orphans.Add(new RuntimeConfigOverlayOrphan(app.Slug, expectedFilePath));
            }
        }

        return new RuntimeConfigOverlayOrphanOutcome
        (
            orphans.Count > 0
                ? RuntimeConfigOverlayOrphanStatus.OrphansFound
                : RuntimeConfigOverlayOrphanStatus.Ok,
            orphans
        );
    }

    // Mirrors RuntimeConfigFileWriter.ResolveTargetPath: strip a single leading
    // '/' or '\\' so Path.Combine treats the config path as relative to the
    // writable root rather than returning the absolute path.
    private static string ResolveExpectedFilePath(string writableRoot, string configPath) =>
        Path.Combine(writableRoot, configPath.TrimStart('/', '\\'));
}
