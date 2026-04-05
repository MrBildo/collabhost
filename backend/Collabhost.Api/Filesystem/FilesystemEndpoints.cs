using System.Security;

using Collabhost.Api.Registry;

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

    private static IResult DetectStrategy(string? path, string? appTypeSlug)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return TypedResults.Problem("path is required.", statusCode: 400);
        }

        if (string.IsNullOrWhiteSpace(appTypeSlug))
        {
            return TypedResults.Problem("appTypeSlug is required.", statusCode: 400);
        }

        if (!IsValidPath(path))
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

        var (strategy, evidence) = DetectStrategyForAppType(fullPath, appTypeSlug);

        return TypedResults.Ok(new DetectStrategyResponse(strategy, evidence));
    }

    private static (string Strategy, List<string> Evidence) DetectStrategyForAppType
    (
        string directory,
        string appTypeSlug
    )
    {
        if (string.Equals(appTypeSlug, "dotnet-app", StringComparison.Ordinal))
        {
            // Prefer runtime config (published output) over project file (source)
            var runtimeConfigs = Directory.GetFiles(directory, "*.runtimeconfig.json");

            if (runtimeConfigs.Length > 0)
            {
                return
                (
                    FormatStrategyName(DiscoveryStrategy.DotNetRuntimeConfiguration),
                    [.. runtimeConfigs.Select(f => Path.GetFileName(f))]
                );
            }

            var projects = Directory.GetFiles(directory, "*.csproj");

            if (projects.Length > 0)
            {
                return
                (
                    FormatStrategyName(DiscoveryStrategy.DotNetProject),
                    [.. projects.Select(f => Path.GetFileName(f))]
                );
            }

            return (FormatStrategyName(DiscoveryStrategy.Manual), []);
        }

        if (string.Equals(appTypeSlug, "nodejs-app", StringComparison.Ordinal))
        {
            var packageJsonPath = Path.Combine(directory, "package.json");

            if (File.Exists(packageJsonPath))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(packageJsonPath));

                    if (document.RootElement.TryGetProperty("scripts", out var scripts)
                        && scripts.TryGetProperty("start", out _))
                    {
                        return (FormatStrategyName(DiscoveryStrategy.PackageJson), ["package.json"]);
                    }
                }
                catch (JsonException)
                {
                    // Malformed package.json -- fall through to Manual
                }

                return (FormatStrategyName(DiscoveryStrategy.Manual), ["package.json"]);
            }

            return (FormatStrategyName(DiscoveryStrategy.Manual), []);
        }

        return (FormatStrategyName(DiscoveryStrategy.Manual), []);
    }

    private static string FormatStrategyName(DiscoveryStrategy strategy)
    {
        var name = strategy.ToString();

        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static bool IsValidPath(string path)
    {
        var invalidChars = Path.GetInvalidPathChars();

        return !path.AsSpan().ContainsAny(invalidChars);
    }
}
