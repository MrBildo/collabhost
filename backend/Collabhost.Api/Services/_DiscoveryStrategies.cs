using System.Collections.Frozen;
using System.Text.Json;

using Collabhost.Api.Domain.Capabilities;
using Collabhost.Api.Domain.Catalogs;

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

    DiscoveredProcess Discover(ProcessConfiguration processConfiguration, string effectiveWorkingDirectory);
}

public sealed class DotNetRuntimeConfigDiscoveryStrategy : IDiscoveryStrategy
{
    public string StrategyName => StringCatalog.DiscoveryStrategies.DotNetRuntimeConfig;

    public DiscoveredProcess Discover(ProcessConfiguration processConfiguration, string effectiveWorkingDirectory)
    {
        if (string.IsNullOrWhiteSpace(effectiveWorkingDirectory))
        {
            throw new InvalidOperationException
            (
                "Cannot use dotnet-runtimeconfig discovery strategy: " +
                "Artifact location is not configured. Set the artifact capability's location field."
            );
        }

        var runtimeConfigFiles = Directory.GetFiles(effectiveWorkingDirectory, "*.runtimeconfig.json");

        if (runtimeConfigFiles.Length == 0)
        {
            throw new InvalidOperationException
            (
                $"No *.runtimeconfig.json file found in '{effectiveWorkingDirectory}'. " +
                "Ensure the application has been published or built."
            );
        }

        var runtimeConfigPath = runtimeConfigFiles[0];
        var dllName = Path
            .GetFileNameWithoutExtension(runtimeConfigPath)
            .Replace(".runtimeconfig", "", StringComparison.OrdinalIgnoreCase) + ".dll";

        return new DiscoveredProcess("dotnet", dllName, effectiveWorkingDirectory);
    }
}

public sealed class PackageJsonDiscoveryStrategy : IDiscoveryStrategy
{
    public string StrategyName => StringCatalog.DiscoveryStrategies.PackageJson;

    public DiscoveredProcess Discover(ProcessConfiguration processConfiguration, string effectiveWorkingDirectory)
    {
        if (string.IsNullOrWhiteSpace(effectiveWorkingDirectory))
        {
            throw new InvalidOperationException
            (
                "Cannot use package-json discovery strategy: " +
                "Artifact location is not configured. Set the artifact capability's location field."
            );
        }

        var packageJsonPath = Path.Combine(effectiveWorkingDirectory, "package.json");

        if (!File.Exists(packageJsonPath))
        {
            throw new InvalidOperationException
            (
                $"No package.json found at '{packageJsonPath}'."
            );
        }

        // Validate that the package.json has a start script
        var packageJsonContent = File.ReadAllText(packageJsonPath);
        using var document = JsonDocument.Parse(packageJsonContent);

        return document.RootElement.TryGetProperty("scripts", out var scripts)
            && scripts.TryGetProperty("start", out _)
            ? new DiscoveredProcess("npm", "start", effectiveWorkingDirectory)
            : throw new InvalidOperationException
            (
                $"package.json at '{packageJsonPath}' does not have a 'scripts.start' entry."
            );
    }
}

public sealed class ManualDiscoveryStrategy : IDiscoveryStrategy
{
    public string StrategyName => StringCatalog.DiscoveryStrategies.Manual;

    public DiscoveredProcess Discover(ProcessConfiguration processConfiguration, string effectiveWorkingDirectory) =>
        string.IsNullOrWhiteSpace(processConfiguration.Command)
            ? throw new InvalidOperationException
            (
                "Cannot use manual discovery strategy: " +
                "Command is not configured on the process capability."
            )
            : new DiscoveredProcess
            (
                processConfiguration.Command,
                processConfiguration.Arguments,
                effectiveWorkingDirectory
            );
}

public sealed class DiscoveryStrategyFactory
{
    private static readonly FrozenDictionary<string, IDiscoveryStrategy> _strategies =
        new Dictionary<string, IDiscoveryStrategy>(StringComparer.OrdinalIgnoreCase)
        {
            [StringCatalog.DiscoveryStrategies.DotNetRuntimeConfig] = new DotNetRuntimeConfigDiscoveryStrategy(),
            [StringCatalog.DiscoveryStrategies.PackageJson] = new PackageJsonDiscoveryStrategy(),
            [StringCatalog.DiscoveryStrategies.Manual] = new ManualDiscoveryStrategy()
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public IDiscoveryStrategy GetStrategy(string strategyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyName);

        return _strategies.TryGetValue(strategyName, out var strategy)
            ? strategy
            : throw new InvalidOperationException
            (
                $"Unknown discovery strategy '{strategyName}'. " +
                $"Supported strategies: {string.Join(", ", _strategies.Keys)}"
            );
    }
}
