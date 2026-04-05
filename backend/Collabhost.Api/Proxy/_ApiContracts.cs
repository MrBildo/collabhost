namespace Collabhost.Api.Proxy;

// JSON-serialized DTOs -- List<T> is practical for response types
#pragma warning disable MA0016
public record RouteListResponse
(
    List<RouteListEntry> Routes,
    string BaseDomain
);

public record RouteListEntry
(
    string AppExternalId,
    string AppName,
    string AppDisplayName,
    string Domain,
    string Target,
    string ProxyMode,
    bool Https,
    bool Enabled
);
