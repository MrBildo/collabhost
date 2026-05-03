namespace Collabhost.Api.Platform;

public record SystemStatus
(
    string Status,
    string Version,
    string Hostname,
    double UptimeSeconds,
    string Timestamp,
    string ProxyState,
    string PortalUrl,
    bool PortalReachable,
    ProxyDetail? ProxyDetail
);

// Surfaced when the proxy is degraded so operators can see why routes aren't
// reaching the public listener (bind failure, config rejection, etc.). null when
// the proxy is healthy or in a non-degraded terminal state. Card #217.
public record ProxyDetail
(
    bool LastSyncOk,
    string? LastSyncError,
    string? LastSyncAt,
    string ListenAddress
);

public record VersionResponse
(
    string Version,
    string Commit,
    string Platform
);
