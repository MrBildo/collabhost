namespace Collabhost.Api.Proxy;

public class ProxySettings
{
    public const string SectionName = "Proxy";

    public required string BaseDomain { get; init; }

    public required string BinaryPath { get; init; }

    public required string ListenAddress { get; init; }

    public required string CertLifetime { get; init; }

    public required int SelfPort { get; init; }

    // Allocated at startup via PortAllocator -- not from configuration
    public int AdminPort { get; set; }
}
