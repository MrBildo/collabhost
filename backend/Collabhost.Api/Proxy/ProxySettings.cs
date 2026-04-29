namespace Collabhost.Api.Proxy;

public class ProxySettings
{
    public const string SectionName = "Proxy";

    public required string BaseDomain { get; init; }

    // Optional in production: the bundled sidecar at AppContext.BaseDirectory is the default.
    // The COLLABHOST_CADDY_PATH env var is the sanctioned override; absolute paths set here
    // are honored as an undocumented escape hatch but are not part of the operator contract.
    public string? BinaryPath { get; init; }

    public required string ListenAddress { get; init; }

    public required string CertLifetime { get; init; }

    // Allocated at startup via PortAllocator -- not from configuration
    public int AdminPort { get; set; }
}
