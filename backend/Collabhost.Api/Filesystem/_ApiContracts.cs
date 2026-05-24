namespace Collabhost.Api.Filesystem;

public record DirectoryEntry(string Name, string Path);

public record BrowseResponse
(
    string CurrentPath,
    string? Parent,
    IReadOnlyList<DirectoryEntry> Directories
);

// JSON-serialized DTOs -- List<T>/Dictionary are practical for response types
#pragma warning disable MA0016
public record DetectStrategyResponse
(
    string SuggestedStrategy,
    List<string> Evidence
);

// Card #344: returned when the endpoint is called without an appTypeSlug query
// param -- one entry per AppType the collector has detection rules for. Decouples
// callers (notably the App Create form) from having to know the type before they
// can ask "what's in this directory?"
public record DetectStrategyPerTypeResponse
(
    Dictionary<string, DetectStrategyResponse> PerType
);
#pragma warning restore MA0016
