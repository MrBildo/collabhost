using System.ComponentModel;
using System.Security;

using Collabhost.Api.Registry;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Collabhost.Api.Mcp;

[McpServerToolType]
public class RegistrationTools
{
    [McpServerTool
    (
        Name = "browse_filesystem",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Lists directories at a given path on the host machine. When path is omitted, returns the root drives (Windows) or '/' (Unix). Use this during app registration to find the install directory interactively. Returns child directory names and paths. Hidden and system directories are excluded.")]
    public static CallToolResult BrowseFilesystem
    (
        [Description("Absolute filesystem path to list. If omitted, returns root drives/directories.")] string? path
    )
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return BrowseRoots();
        }

        if (!IsValidPath(path))
        {
            return McpResponseFormatter.InvalidParameters
            (
                "The specified path contains invalid characters."
            );
        }

        string fullPath;

        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return McpResponseFormatter.InvalidParameters($"Invalid path: {ex.Message}");
        }

        if (!Directory.Exists(fullPath))
        {
            return McpResponseFormatter.InvalidParameters
            (
                $"Directory '{fullPath}' does not exist."
            );
        }

        IReadOnlyList<object> directories;

        try
        {
            directories = EnumerateChildDirectories(fullPath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            return McpResponseFormatter.InvalidParameters($"Access denied to '{fullPath}'.");
        }

        var parentPath = Directory.GetParent(fullPath)?.FullName;

        var result = new
        {
            currentPath = fullPath,
            parentPath,
            directories
        };

        return McpResponseFormatter.Success(McpResponseFormatter.ToJson(result));
    }

    [McpServerTool
    (
        Name = "detect_strategy",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Analyzes a directory to determine what Collabhost can auto-detect for a given app type. For dotnet-app, checks for .runtimeconfig.json (published output) or .csproj (source project). For nodejs-app, checks for package.json with a start script. Returns the detected strategy name and evidence files found. Use this after choosing an app type and install directory, before calling register_app, to understand what configuration Collabhost will auto-discover vs. what must be specified manually.")]
    public static CallToolResult DetectStrategy
    (
        [Description("Absolute filesystem path to analyze.")] string path,
        [Description("App type slug (e.g., 'dotnet-app', 'nodejs-app'). Use list_app_types to see valid values.")] string appTypeSlug
    )
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return McpResponseFormatter.InvalidParameters("path is required.");
        }

        if (string.IsNullOrWhiteSpace(appTypeSlug))
        {
            return McpResponseFormatter.InvalidParameters("appTypeSlug is required.");
        }

        if (!IsValidPath(path))
        {
            return McpResponseFormatter.InvalidParameters
            (
                "The specified path contains invalid characters."
            );
        }

        string fullPath;

        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return McpResponseFormatter.InvalidParameters($"Invalid path: {ex.Message}");
        }

        if (!Directory.Exists(fullPath))
        {
            return McpResponseFormatter.InvalidParameters
            (
                $"Directory '{fullPath}' does not exist."
            );
        }

        var (strategy, evidence) = DetectStrategyForAppType(fullPath, appTypeSlug);

        var result = new
        {
            strategy,
            evidenceFiles = evidence
        };

        return McpResponseFormatter.Success(McpResponseFormatter.ToJson(result));
    }

    private static CallToolResult BrowseRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            IReadOnlyList<object> drives =
            [
                .. DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                        .Select
                        (
                            d => (object)new
                            {
                                name = d.Name.TrimEnd('\\'),
                                path = d.RootDirectory.FullName
                            }
                        )
            ];

            var rootResult = new
            {
                currentPath = string.Empty,
                parentPath = (string?)null,
                directories = drives
            };

            return McpResponseFormatter.Success(McpResponseFormatter.ToJson(rootResult));
        }

        // Unix: single root
        var unixDirectories = EnumerateChildDirectories("/");

        var unixResult = new
        {
            currentPath = "/",
            parentPath = (string?)null,
            directories = unixDirectories
        };

        return McpResponseFormatter.Success(McpResponseFormatter.ToJson(unixResult));
    }

    private static IReadOnlyList<object> EnumerateChildDirectories(string directoryPath)
    {
        var directory = new DirectoryInfo(directoryPath);

        return
        [
            .. directory
                .EnumerateDirectories()
                .Where
                (
                    d => !d.Attributes.HasFlag(FileAttributes.Hidden)
                        && !d.Attributes.HasFlag(FileAttributes.System)
                )
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(d => (object)new { name = d.Name, path = d.FullName })
        ];
    }

    private static (string Strategy, List<string> Evidence) DetectStrategyForAppType
    (
        string directory,
        string appTypeSlug
    )
    {
        if (string.Equals(appTypeSlug, "dotnet-app", StringComparison.Ordinal))
        {
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
