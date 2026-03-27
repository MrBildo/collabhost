namespace Collabhost.Api.Services;

public class ProxySettings
{
    public required string BaseDomain { get; init; } = "collab.internal";
    public required string AdminApiUrl { get; init; } = "http://localhost:2019";
    public required string BinaryPath { get; init; } = "caddy";
    public required string ListenAddress { get; init; } = ":443";
    public required string CertLifetime { get; init; } = "168h";
    public required int SelfPort { get; init; } = 58400;
}
