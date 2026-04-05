using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Registry;

namespace Collabhost.Api.Supervisor;

public static class DiscoveryStrategyExecutor
{
    public static DiscoveredProcess Discover
    (
        ProcessConfiguration configuration,
        string workingDirectory
    ) =>
        configuration.DiscoveryStrategy switch
        {
            DiscoveryStrategy.DotNetRuntimeConfiguration => DiscoverDotNetApplication(workingDirectory),
            DiscoveryStrategy.DotNetProject => DiscoverDotNetProject(workingDirectory),
            DiscoveryStrategy.PackageJson => DiscoverNodeApplication(workingDirectory),
            DiscoveryStrategy.Manual => new DiscoveredProcess
            (
                configuration.Command
                    ?? throw new InvalidOperationException("Command is required for Manual discovery."),
                configuration.Arguments,
                configuration.WorkingDirectory ?? workingDirectory
            ),
            _ => throw new InvalidOperationException
            (
                $"Unknown discovery strategy: {configuration.DiscoveryStrategy}"
            )
        };

    private static DiscoveredProcess DiscoverDotNetApplication(string directory)
    {
        var configurations = Directory.GetFiles(directory, "*.runtimeconfig.json");

        if (configurations.Length == 0)
        {
            throw new InvalidOperationException($"No *.runtimeconfig.json found in '{directory}'.");
        }

        var dllName = Path.GetFileNameWithoutExtension(configurations[0])
            .Replace(".runtimeconfig", "", StringComparison.Ordinal) + ".dll";

        return new DiscoveredProcess("dotnet", dllName, directory);
    }

    private static DiscoveredProcess DiscoverDotNetProject(string directory)
    {
        var projects = Directory.GetFiles(directory, "*.csproj");

        if (projects.Length == 0)
        {
            throw new InvalidOperationException($"No *.csproj found in '{directory}'.");
        }

        if (projects.Length > 1)
        {
            var names = string.Join
            (
                ", ", projects.Select(p => Path.GetFileName(p))
            );

            throw new InvalidOperationException
            (
                $"Multiple *.csproj files found in '{directory}': {names}. "
                + "Use Manual strategy or specify the project file explicitly."
            );
        }

        var projectFile = Path.GetFileName(projects[0]);

        return new DiscoveredProcess("dotnet", $"run --project {projectFile}", directory);
    }

    private static DiscoveredProcess DiscoverNodeApplication(string directory)
    {
        var packageJsonPath = Path.Combine(directory, "package.json");

        if (!File.Exists(packageJsonPath))
        {
            throw new InvalidOperationException($"No package.json found at '{packageJsonPath}'.");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(packageJsonPath));

        return !document.RootElement.TryGetProperty("scripts", out var scripts)
            || !scripts.TryGetProperty("start", out _)
            ? throw new InvalidOperationException("package.json has no scripts.start.")
            : new DiscoveredProcess("npm", "start", directory);
    }
}

public record DiscoveredProcess(string Command, string? Arguments, string WorkingDirectory);
