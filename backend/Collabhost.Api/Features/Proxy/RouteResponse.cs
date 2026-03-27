namespace Collabhost.Api.Features.Proxy;

public record RouteListResponse(IReadOnlyList<RouteEntry> Routes, string BaseDomain);

public record RouteEntry
(
    string AppExternalId,
    string AppName,
    string Domain,
    string Target,
    string ProxyMode,
    bool Https
);

public record ProxyStatusResponse
(
    string State,
    int? Pid,
    bool AdminApiReady,
    int RouteCount,
    string BaseDomain
);
