namespace Collabhost.Api.Filesystem;

public record DirectoryEntry(string Name, string Path);

public record BrowseResponse
(
    string CurrentPath,
    string? Parent,
    IReadOnlyList<DirectoryEntry> Directories
);

// JSON-serialized DTOs -- List<T> is practical for response types
#pragma warning disable MA0016
public record DetectStrategyResponse
(
    string SuggestedStrategy,
    List<string> Evidence
);
#pragma warning restore MA0016
