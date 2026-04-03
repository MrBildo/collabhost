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

    private static DiscoveredProcess DiscoverNodeApplication(string directory)
    {
        var packageJsonPath = Path.Combine(directory, "package.json");

        if (!File.Exists(packageJsonPath))
        {
            throw new InvalidOperationException($"No package.json found at '{packageJsonPath}'.");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(packageJsonPath));

        if (!document.RootElement.TryGetProperty("scripts", out var scripts)
            || !scripts.TryGetProperty("start", out _))
        {
            throw new InvalidOperationException("package.json has no scripts.start.");
        }

        return new DiscoveredProcess("npm", "start", directory);
    }
}

public record DiscoveredProcess(string Command, string? Arguments, string WorkingDirectory);
