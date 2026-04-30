namespace Collabhost.Api.Platform;

// Settings for the Collabhost API process itself -- the port Kestrel listens on, and
// (eventually) any other host-process concerns. Keeping this separate from ProxySettings
// reflects the actual semantics: ListenPort is "the port the Collabhost process listens on,"
// not "a Caddy-scoped configuration value." Caddy reads the same value to know what to dial
// for the dashboard self-route, but the source of truth is hosting.
public class HostingSettings
{
    public const string SectionName = "Hosting";

    public required int ListenPort { get; init; }
}
