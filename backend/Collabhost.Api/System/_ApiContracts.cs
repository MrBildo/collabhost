namespace Collabhost.Api.Platform;

public sealed record SystemStatus
(
    string Status,
    string Version,
    string Hostname,
    double UptimeSeconds,
    string Timestamp
);
