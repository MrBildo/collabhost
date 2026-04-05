namespace Collabhost.Api.Filesystem;

public record DirectoryEntry(string Name, string Path);

public record BrowseResponse
(
    string CurrentPath,
    string? Parent,
    IReadOnlyList<DirectoryEntry> Directories
);
