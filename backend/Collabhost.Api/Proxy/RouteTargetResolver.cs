using System.Globalization;

using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Registry;

namespace Collabhost.Api.Proxy;

// Single source of truth for the operator-facing "upstream target" string a route
// surface renders for an app (Card #435). Before this it was duplicated near-verbatim
// across 4 surfaces -- REST App Detail (AppEndpoints), REST Routes table
// (ProxyEndpoints), MCP list_routes (ConfigurationTools), MCP get_app (DiscoveryTools)
// -- which is the parallel-surface-drift class that let MCP-04 ship (get_app fell
// through to "not-running" for a healthy external-route while the other three
// synthesized the upstream). A 5th serve mode or an upstream annotation now touches
// one function, not four.
//
// Lives in Proxy because Proxy owns "what upstream does Caddy dial for this app" --
// the target string is the operator-facing rendering of the same upstream decision
// ProxyConfigurationBuilder bakes into the actual Caddy route. Pure function, no I/O,
// mirroring ProxyConfigurationBuilder.Build: callers resolve their own
// ExternalTargetConfiguration (they already do) and supply the live process port.
public static class RouteTargetResolver
{
    // Resolution faithful to the pre-dedup branches:
    //   ReverseProxy + external-target, host non-blank & port > 0 -> "{scheme}://{host}:{port}"
    //   ReverseProxy + external-target, otherwise                 -> "not-configured"
    //   ReverseProxy, no external-target, port allocated          -> "localhost:{port}"
    //   ReverseProxy, no external-target, no port                 -> "not-running"
    //   FileServer                                                -> "Static Files"
    //
    // hasExternalTarget is passed separately from the resolved externalTarget so the
    // capability-present (the binding exists) vs. config-resolved (the merged config is
    // non-null and valid) distinction the surfaces make is preserved exactly: an
    // external-route app with a missing/blank upstream renders "not-configured", never
    // the supervised-process "not-running"/"localhost:" branch.
    //
    // Card #435 convergence (operator ruling B): the surfaces previously diverged on the
    // file-server label -- App Detail + both MCP tools emitted the raw Caddy handler name
    // "file-server", while the Routes table already used the operator-friendly "Static
    // Files". The parity contract requires one string per app; the chosen value is "Static
    // Files" because the vendor-abstraction rule forbids leaking the Caddy handler name
    // into operator-facing output. This converges App Detail route.target and both MCP
    // route targets from "file-server" to "Static Files" (a ratified operator-facing +
    // MCP-contract string change) and leaves the Routes table exactly as it already read.
    public static string ResolveTarget
    (
        RoutingConfiguration routing,
        bool hasExternalTarget,
        ExternalTargetConfiguration? externalTarget,
        int? processPort
    )
    {
        ArgumentNullException.ThrowIfNull(routing);

        return routing.ServeMode != ServeMode.ReverseProxy
            ? "Static Files"
            : hasExternalTarget
                ? ResolveExternalTarget(externalTarget)
                : ResolveSupervisedTarget(processPort);
    }

    private static string ResolveExternalTarget(ExternalTargetConfiguration? externalTarget) =>
        externalTarget is not null
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

    private static string ResolveSupervisedTarget(int? processPort) =>
        processPort is not null
            ? string.Create(CultureInfo.InvariantCulture, $"localhost:{processPort.Value}")
            : "not-running";
}
