using System.Security;

namespace Collabhost.Api.Filesystem;

public static class FilesystemEndpoints
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/filesystem").WithTags("Filesystem");

        group.MapGet("/browse", Browse);
    }

    private static IResult Browse(string? path)
    {
        // When path is omitted or empty, return system drive roots
        if (string.IsNullOrWhiteSpace(path))
        {
            return TypedResults.Ok(GetDriveRoots());
        }

        // Validate the path is syntactically valid
        if (!IsValidPath(path))
        {
            return TypedResults.Problem
            (
                "The specified path contains invalid characters.",
                statusCode: 400
            );
        }

        // Normalize the path
        string fullPath;

        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return TypedResults.Problem
            (
                $"Invalid path: {ex.Message}",
                statusCode: 400
            );
        }

        if (!Directory.Exists(fullPath))
        {
            return TypedResults.Problem
            (
                $"The directory '{fullPath}' does not exist.",
                statusCode: 404
            );
        }

        IReadOnlyList<DirectoryEntry> directories;

        try
        {
            directories = EnumerateChildDirectories(fullPath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            return TypedResults.Problem
            (
                $"Access denied to '{fullPath}'.",
                statusCode: 403
            );
        }

        var parentPath = Directory.GetParent(fullPath)?.FullName;

        return TypedResults.Ok(new BrowseResponse(fullPath, parentPath, directories));
    }

    private static BrowseResponse GetDriveRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            IReadOnlyList<DirectoryEntry> drives =
            [
                .. DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                        .Select(d => new DirectoryEntry(d.Name.TrimEnd('\\'), d.RootDirectory.FullName))
                        .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            ];

            return new BrowseResponse(string.Empty, null, drives);
        }

        // Unix: single root
        return new BrowseResponse("/", null, EnumerateChildDirectories("/"));
    }

    private static IReadOnlyList<DirectoryEntry> EnumerateChildDirectories(string directoryPath)
    {
        var directory = new DirectoryInfo(directoryPath);

        return
        [
            .. directory
                .EnumerateDirectories()
                .Where(d => !d.Attributes.HasFlag(FileAttributes.Hidden) && !d.Attributes.HasFlag(FileAttributes.System))
                    .Select(d => new DirectoryEntry(d.Name, d.FullName))
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
        ];
    }

    private static bool IsValidPath(string path)
    {
        var invalidChars = Path.GetInvalidPathChars();

        return !path.AsSpan().ContainsAny(invalidChars);
    }
}
