namespace Collabhost.Api.Platform;

public record SystemStatus
(
    string Status,
    string Version,
    string Hostname,
    double UptimeSeconds,
    string Timestamp
);

public record VersionResponse
(
    string Version,
    string Commit,
    string Platform
);
