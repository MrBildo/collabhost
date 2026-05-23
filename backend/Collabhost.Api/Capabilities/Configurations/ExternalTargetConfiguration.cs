namespace Collabhost.Api.Capabilities.Configurations;

// External upstream target for an external-route app type (Card #348).
//
// external-route is a routing-only app shape: Collabhost does not manage the
// process behind the upstream. The operator declares the upstream's network
// address (host + port + scheme) via this capability; ProxyManager threads
// it into a Caddy reverse_proxy route. The capability has no analog of port-
// injection because there is no Collabhost-allocated port to inject -- the
// operator is the source of truth.
//
// host is validated by CapabilityResolver against
// ExternalTargetHostPatternString unless ExternalTarget:AllowPublicHosts is on.
public class ExternalTargetConfiguration
{
    public string Host { get; set; } = "";

    public int Port { get; set; }

    // "http" or "https". Default "http" -- the LAN / container / Tailscale
    // common case where the upstream is plain HTTP and Caddy handles public
    // TLS termination. "https" is required only when the upstream itself
    // speaks TLS (e.g. a self-signed endpoint operators front with Caddy
    // doing the public-cert side). Card #348, D2.
    public string Scheme { get; set; } = "http";

    public static IReadOnlyList<FieldDescriptor> Schema =>
    [
        new
        (
            "host",
            "Target Host",
            FieldType.Text,
            new FieldEditableAlways(),
            Required: true,
            RequiresRestart: true,
            HelpText: "Hostname or IP of the upstream service. Examples: "
                + "'localhost' (a process Collabhost shares a network namespace with), "
                + "'192.168.1.50' (a LAN host), 'crawl4ai' (a Docker compose service "
                + "name when Collabhost is on the same Docker network). The platform "
                + "rejects public hostnames by default; set "
                + "'ExternalTarget:AllowPublicHosts = true' in appsettings to opt in.",
            ValuePattern: CapabilityResolver.ExternalTargetHostPatternString,
            ValuePatternMessage: CapabilityResolver.ExternalTargetHostPatternMessage
        ),
        new
        (
            "port",
            "Target Port",
            FieldType.Number,
            new FieldEditableAlways(),
            Required: true,
            RequiresRestart: true,
            HelpText: "TCP port of the upstream service (1-65535).",
            MinValue: 1,
            MaxValue: 65535
        ),
        new
        (
            "scheme",
            "Scheme",
            FieldType.Select,
            new FieldEditableAlways(),
            RequiresRestart: true,
            HelpText: "Almost always 'http' for LAN / container targets -- Caddy "
                + "handles public TLS termination. Use 'https' only when the "
                + "upstream itself speaks TLS.",
            Options:
            [
                new("http", "http"),
                new("https", "https")
            ]
        )
    ];
}
