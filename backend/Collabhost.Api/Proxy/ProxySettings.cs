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

    public required string CertLifetime { get; init; }

    // Allocated at startup via PortAllocator -- not from configuration
    public int AdminPort { get; set; }
}
