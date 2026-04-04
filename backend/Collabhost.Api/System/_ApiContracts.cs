namespace Collabhost.Api.Platform;

public record SystemStatus
(
    string Status,
    string Version,
    string Hostname,
    double UptimeSeconds,
    string Timestamp
);
