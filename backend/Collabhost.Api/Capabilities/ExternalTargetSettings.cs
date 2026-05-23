namespace Collabhost.Api.Capabilities;

// Operator-facing policy lever for the external-target capability (Card #348, D3).
//
// When false (default), CapabilityResolver.ValidateEdits enforces
// CapabilityResolver.ExternalTargetHostPatternString on external-target.host --
// restricting external-route upstreams to loopback / RFC1918 / link-local IPv4
// addresses and *.local / *.lan / IPv6 loopback hostnames. The platform's stance
// is single-operator-dogfood / homelab; the first operator who tries to front
// api.openai.com discovers the platform has an opinion.
//
// When true, the strict-anchor host pattern is bypassed and a permissive
// hostname-shape fallback is applied (any non-empty string that looks like a
// hostname). Operators flip this to register public hostnames as upstream
// targets -- for example, fronting an external SaaS endpoint behind a
// {slug}.collab.<domain> route. The flip is opt-in because the platform
// cannot reason about whether the operator MEANT to front a public host.
public class ExternalTargetSettings
{
    public const string SectionName = "ExternalTarget";

    public required bool AllowPublicHosts { get; init; }
}
