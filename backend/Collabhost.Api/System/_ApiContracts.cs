namespace Collabhost.Api.Platform;

public record SystemStatus
(
    string Status,
    string Version,
    string Hostname,
    double UptimeSeconds,
    string Timestamp,
    string ProxyState,
    string PortalUrl
);

public record VersionResponse
(
    string Version,
    string Commit,
    string Platform
);
