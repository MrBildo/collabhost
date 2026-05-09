namespace Collabhost.Api.Platform;

// Settings for the Collabhost API process itself -- the address and port Kestrel listens on,
// and (eventually) any other host-process concerns. Keeping this separate from ProxySettings
// reflects the actual semantics: ListenAddress / ListenPort are "where the Collabhost process
// listens," not "Caddy-scoped configuration values." Caddy reads ListenPort to know what to
// dial for the dashboard self-route, but the source of truth is hosting.
//
// ListenAddress defaults to "localhost" so the canonical posture is unchanged: edge TLS
// terminates at Caddy on :443 and the API stays loopback-only. Headless-server installs that
// reach the dashboard directly (no DNS for *.collab.internal, no CA trust for the bundled
// internal CA) can set ListenAddress=0.0.0.0 to expose the API on every interface, or pin it
// to a specific NIC IP. Card #218.
public class HostingSettings
{
    public const string SectionName = "Hosting";

    public required string ListenAddress { get; init; }

    public required int ListenPort { get; init; }
}
