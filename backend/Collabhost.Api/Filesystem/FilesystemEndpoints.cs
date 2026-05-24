using System.Security;

using Collabhost.Api.Probes;
using Collabhost.Api.Shared;

namespace Collabhost.Api.Filesystem;

public static class FilesystemEndpoints
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/filesystem").WithTags("Filesystem");

        group.MapGet("/browse", Browse);
        group.MapGet("/detect-strategy", DetectStrategy);
    }

    private static IResult Browse(string? path)
    {
        // When path is omitted or empty, return system drive roots
        if (string.IsNullOrWhiteSpace(path))
        {
            return TypedResults.Ok(GetDriveRoots());
        }

        // Validate the path is syntactically valid
        if (!path.IsValidPath())
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

    // Two response shapes:
    //   - appTypeSlug provided: single { suggestedStrategy, evidence } per the
    //     pre-#344 wire contract (preserved verbatim for v1.0.x backward
    //     compatibility with existing FE / MCP / operator scripts).
    //   - appTypeSlug omitted: { perType: { <slug>: { suggestedStrategy,
    //     evidence } } } -- one entry for each AppType the collector has
    //     detection rules for. Card #344 -- decouples callers from form-step
    //     ordering by letting them ask "what's in this directory?" before
    //     knowing the AppType.
    private static IResult DetectStrategy(string? path, string? appTypeSlug)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return TypedResults.Problem("path is required.", statusCode: 400);
        }

        if (!path.IsValidPath())
        {
            return TypedResults.Problem
            (
                "The specified path contains invalid characters.",
                statusCode: 400
            );
        }

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

        // Slug provided -> single-type response (pre-#344 wire shape, preserved
        // for v1.0.x BC). Slug omitted -> per-type map (one entry per AppType
        // the collector handles), card #344.
        object payload = string.IsNullOrWhiteSpace(appTypeSlug)
            ? CollectPerType(fullPath)
            : CollectSingle(fullPath, appTypeSlug);

        return Results.Ok(payload);
    }

    private static DetectStrategyResponse CollectSingle(string fullPath, string appTypeSlug)
    {
        var evidence = ArtifactEvidenceCollector.Collect(fullPath, appTypeSlug);

        return new DetectStrategyResponse(evidence.SuggestedStrategy, ToPaths(evidence));
    }

    private static DetectStrategyPerTypeResponse CollectPerType(string fullPath)
    {
        var perType = new Dictionary<string, DetectStrategyResponse>(StringComparer.Ordinal);

        foreach (var slug in ArtifactEvidenceCollector.KnownAppTypeSlugs)
        {
            var evidence = ArtifactEvidenceCollector.Collect(fullPath, slug);
            perType[slug] = new DetectStrategyResponse(evidence.SuggestedStrategy, ToPaths(evidence));
        }

        return new DetectStrategyPerTypeResponse(perType);
    }

    private static List<string> ToPaths(ArtifactEvidence evidence)
    {
        var paths = new List<string>(evidence.Signals.Count);

        foreach (var signal in evidence.Signals)
        {
            paths.Add(signal.Path);
        }

        return paths;
    }
}
