using System.Collections.Frozen;
using System.Text.Json;

using Collabhost.Api.Domain.Capabilities;

namespace Collabhost.Api.Services;

public record DiscoveredProcess
(
    string Command,
    string? Arguments,
    string? WorkingDirectory
);

public interface IDiscoveryStrategy
{
    string StrategyName { get; }

    DiscoveredProcess Discover(ProcessConfiguration processConfiguration);
}

public sealed class DotNetRuntimeConfigDiscoveryStrategy : IDiscoveryStrategy
{
    public string StrategyName => "dotnet-runtimeconfig";

    public DiscoveredProcess Discover(ProcessConfiguration processConfiguration)
    {
        var workingDirectory = processConfiguration.WorkingDirectory;

        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new InvalidOperationException(
                "Cannot use dotnet-runtimeconfig discovery strategy: " +
                "WorkingDirectory is not configured on the process capability. " +
                "This will be resolved when the artifact capability is implemented.");
        }

        var runtimeConfigFiles = Directory.GetFiles(workingDirectory, "*.runtimeconfig.json");

        if (runtimeConfigFiles.Length == 0)
        {
            throw new InvalidOperationException(
                $"No *.runtimeconfig.json file found in '{workingDirectory}'. " +
                "Ensure the application has been published or built.");
        }

        var runtimeConfigPath = runtimeConfigFiles[0];
        var dllName = Path.GetFileNameWithoutExtension(runtimeConfigPath)
            .Replace(".runtimeconfig", "", StringComparison.OrdinalIgnoreCase) + ".dll";

        return new DiscoveredProcess("dotnet", dllName, workingDirectory);
    }
}

public sealed class PackageJsonDiscoveryStrategy : IDiscoveryStrategy
{
    public string StrategyName => "package-json";

    public DiscoveredProcess Discover(ProcessConfiguration processConfiguration)
    {
        var workingDirectory = processConfiguration.WorkingDirectory;

        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new InvalidOperationException(
                "Cannot use package-json discovery strategy: " +
                "WorkingDirectory is not configured on the process capability. " +
                "This will be resolved when the artifact capability is implemented.");
        }

        var packageJsonPath = Path.Combine(workingDirectory, "package.json");

        if (!File.Exists(packageJsonPath))
        {
            throw new InvalidOperationException(
                $"No package.json found at '{packageJsonPath}'.");
        }

        // Validate that the package.json has a start script
        var packageJsonContent = File.ReadAllText(packageJsonPath);
        using var document = JsonDocument.Parse(packageJsonContent);

        return document.RootElement.TryGetProperty("scripts", out var scripts)
            && scripts.TryGetProperty("start", out _)
            ? new DiscoveredProcess("npm", "start", workingDirectory)
            : throw new InvalidOperationException(
                $"package.json at '{packageJsonPath}' does not have a 'scripts.start' entry.");
    }
}

public sealed class ManualDiscoveryStrategy : IDiscoveryStrategy
{
    public string StrategyName => "manual";

    public DiscoveredProcess Discover(ProcessConfiguration processConfiguration) =>
        string.IsNullOrWhiteSpace(processConfiguration.Command)
            ? throw new InvalidOperationException(
                "Cannot use manual discovery strategy: " +
                "Command is not configured on the process capability.")
            : new DiscoveredProcess(
                processConfiguration.Command,
                processConfiguration.Arguments,
                processConfiguration.WorkingDirectory);
}

public sealed class DiscoveryStrategyFactory
{
    private static readonly FrozenDictionary<string, IDiscoveryStrategy> _strategies =
        new Dictionary<string, IDiscoveryStrategy>(StringComparer.OrdinalIgnoreCase)
        {
            ["dotnet-runtimeconfig"] = new DotNetRuntimeConfigDiscoveryStrategy(),
            ["package-json"] = new PackageJsonDiscoveryStrategy(),
            ["manual"] = new ManualDiscoveryStrategy()
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public IDiscoveryStrategy GetStrategy(string strategyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyName);

        return _strategies.TryGetValue(strategyName, out var strategy)
            ? strategy
            : throw new InvalidOperationException(
                $"Unknown discovery strategy '{strategyName}'. " +
                $"Supported strategies: {string.Join(", ", _strategies.Keys)}");
    }
}
