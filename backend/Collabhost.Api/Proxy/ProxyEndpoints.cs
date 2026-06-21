using System.Globalization;

using Collabhost.Api.Authorization;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Operations;
using Collabhost.Api.Platform;
using Collabhost.Api.Portal;
using Collabhost.Api.Registry;
using Collabhost.Api.Supervisor;

namespace Collabhost.Api.Proxy;

#pragma warning disable MA0076 // Ulid.ToString is not locale-sensitive
#pragma warning disable MA0011 // Ulid.ToString is not locale-sensitive
public static class ProxyEndpoints
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1").WithTags("Proxy");

        // Mixed read+write group: /routes is an open read; the proxy reload is a control-plane
        // mutation gated to Agent (per-route, matching the MCP reload_proxy entitlement).
        group.MapGet("/routes", ListRoutesAsync);
        group
            .MapPost("/proxy/reload", ReloadProxyAsync)
            .AddEndpointFilter(new RequireRoleFilter(UserRole.Agent));
    }

    private static async Task<IResult> ListRoutesAsync
    (
        AppStore store,
        TypeStore typeStore,
        ProcessSupervisor supervisor,
        ProxyManager proxy,
        ProxySettings settings,
        PortalSettings portalSettings,
        HostingSettings hostingSettings,
        CancellationToken ct
    )
    {
        var apps = await store.ListAsync(ct);

        var entries = new List<RouteListEntry>
        {
            // Synthetic Portal row pinned at index 0. The Portal is not a registered app
            // (no App entity, no AppType, no ProxyAppSeeder); the row is synthesized so the
            // operator can see the resolved Portal hostname without curling Caddy admin.
            // IsPortal flags the row so the frontend can render it distinctly without
            // empty-AppExternalId sniffing. Card #184.
            new
            (
                AppExternalId: string.Empty,
                AppName: "collabhost",
                AppDisplayName: "Collabhost Portal",
                Domain: $"{portalSettings.Subdomain}.{settings.BaseDomain}",
                Target: $"localhost:{hostingSettings.ListenPort.ToString(CultureInfo.InvariantCulture)}",
                ProxyMode: "reverseProxy",
                Https: true,
                Enabled: true,
                IsPortal: true
            )
        };

        foreach (var app in apps)
        {
            var bindings = typeStore.GetBindings(app.AppTypeSlug);

            if (bindings is null || !bindings.TryGetValue("routing", out var routingBindingJson))
            {
                continue;
            }

            var overrides = await store.GetOverridesAsync(app.Id, ct);

            var overrideJson = overrides.TryGetValue("routing", out var routingOverride)
                ? routingOverride.ConfigurationJson
                : null;

            var routingConfiguration = CapabilityResolver.Resolve<RoutingConfiguration>
            (
                routingBindingJson, overrideJson
            );

            var domain = CapabilityResolver.ResolveDomain
            (
                routingConfiguration.DomainPattern, app.Slug, settings.BaseDomain
            );

            var process = supervisor.GetProcess(app.Id);

            // Card #348 polish: external-route apps have no supervised process.
            // Synthesize the operator-declared upstream so the Routes table matches
            // what Caddy is actually dialing -- same resolution pattern used on the
            // App Detail page (AppEndpoints.GetAppDetailAsync).
            var hasExternalTarget = bindings.ContainsKey("external-target");

            string target;

            if (routingConfiguration.ServeMode == ServeMode.ReverseProxy)
            {
                if (hasExternalTarget)
                {
                    var externalTarget = bindings.TryGetValue("external-target", out var externalTargetBinding)
                        ? CapabilityResolver.Resolve<ExternalTargetConfiguration>
                        (
                            externalTargetBinding,
                            overrides.TryGetValue("external-target", out var externalTargetOverride)
                                ? externalTargetOverride.ConfigurationJson
                                : null
                        )
                        : null;

                    target = externalTarget is not null
                        && !string.IsNullOrWhiteSpace(externalTarget.Host)
                        && externalTarget.Port > 0
                            ? string.Format
                            (
                                CultureInfo.InvariantCulture,
                                "{0}://{1}:{2}",
                                externalTarget.Scheme,
                                externalTarget.Host,
                                externalTarget.Port
                            )
                            : "not-configured";
                }
                else
                {
                    target = process?.Port is not null
                        ? $"localhost:{process.Port.Value.ToString(CultureInfo.InvariantCulture)}"
                        : "not-running";
                }
            }
            else
            {
                target = "Static Files";
            }

            entries.Add
            (
                new RouteListEntry
                (
                    app.Id.ToString(),
                    app.Slug,
                    app.DisplayName,
                    domain,
                    target,
                    routingConfiguration.ServeMode == ServeMode.ReverseProxy
                        ? "reverseProxy"
                        : "fileServer",
                    true,
                    proxy.IsRouteEnabled(app.Slug),
                    IsPortal: false
                )
            );
        }

        // Route table reflects proxyState so the frontend can annotate per-row UX (e.g.,
        // grey out / strike the Portal row's hostname when the public listener isn't bound).
        // Per-row Enabled stays as operator intent; proxyState is the operational reality. #217.
        var proxyState = proxy.CurrentState;
        var proxyStateString = proxyState
            .ToString()
            .ToLowerInvariant();
        var portalReachable = proxyState == ProxyState.Running;

        return TypedResults.Ok
        (
            new RouteListResponse(entries, settings.BaseDomain, proxyStateString, portalReachable)
        );
    }

    // Migrated to the operation spine (code-structure-conventions §8): the endpoint is a thin
    // adapter -- inject ReloadProxyOperation directly (no dispatcher), call it with the marker
    // command (no slug to adapt -- the reload acts on no app), and map OperationResult back to
    // exactly the 204 No Content this handler returned before. The proxy.RequestSync() + the
    // actor-stamped proxy.reloaded event now live once inside the operation.
    private static async Task<IResult> ReloadProxyAsync
    (
        ReloadProxyOperation operation,
        CancellationToken ct
    )
    {
        var result = await operation.ExecuteAsync(new ReloadProxyCommand(), ct);

        return result.ToHttpResult();
    }
}

// File-scoped mapping from the surface-agnostic reload outcome back to the REST result shape
// (§7: the surface holds only its file-scoped mapping, never the contract types). K-1 (Kai's PR-1
// forward note): OperationResult.FailureKind defaults to ordinal-0 NotFound on a success, so the
// success arm is gated on IsSuccess FIRST -- FailureKind is only read on the failure path. The
// success arm -> 204 No Content. The reload now HAS a failure path: when the proxy is disabled
// the operation returns Conflict, which maps to 409 (the `_` arm) carrying the operator-actionable
// "proxy is disabled" message, so a reload against a dead proxy signals rather than false-succeeds.
file static class ReloadProxyResultMapping
{
    public static IResult ToHttpResult(this OperationResult<ProxyReloadOutcome> result) =>
        result.IsSuccess
            ? TypedResults.NoContent()
            : result.FailureKind switch
            {
                OperationFailureKind.NotFound => TypedResults.NotFound(),
                OperationFailureKind.Validation => TypedResults.Problem(result.Error, statusCode: 400),
                _ => TypedResults.Problem(result.Error, statusCode: 409),
            };
}
#pragma warning restore MA0011
#pragma warning restore MA0076
