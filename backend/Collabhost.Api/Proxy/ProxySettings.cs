namespace Collabhost.Api.Proxy;

public class ProxySettings
{
    public const string SectionName = "Proxy";

    public required string BaseDomain { get; init; }

    // Optional. Resolution order is COLLABHOST_CADDY_PATH env var > this setting > null.
    // Install scripts seed this with the absolute path to the bundled caddy[.exe] on first
    // install. When null/empty/whitespace and the env var is unset, the proxy subsystem
    // boots in the Disabled state.
    public string? BinaryPath { get; init; }

    public required string ListenAddress { get; init; }

    // Lifetime is internal-CA-only. Let's Encrypt rejects custom lifetimes
    // (90 days is fixed by the issuer). When DnsProvider is set the ACME
    // branch of BuildTlsConfiguration omits this value entirely.
    public required string CertLifetime { get; init; }

    // When null/empty, the proxy emits Caddy's internal-CA issuer block (default
    // for collab.internal-style local deployments). When set to a Caddy DNS
    // provider name (e.g. "cloudflare"), the proxy emits an ACME issuer block
    // configured for DNS-01 challenge against that provider. Phase C of card #34.
    public string? DnsProvider { get; init; }

    // Name of the host-process env var that carries the DNS provider's API token.
    // Resolved at child-process-spawn time by ProxyEnvironmentProvider. Defaults
    // to "CLOUDFLARE_API_TOKEN"; only consulted when DnsProvider is set.
    public string? DnsApiTokenEnvVar { get; init; }

    // Allocated at startup via PortAllocator -- not from configuration
    public int AdminPort { get; set; }
}
