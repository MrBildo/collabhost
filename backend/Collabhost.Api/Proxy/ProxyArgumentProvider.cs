using System.Globalization;

using Collabhost.Api.Supervisor;

namespace Collabhost.Api.Proxy;

// Injects the dynamic admin port into Caddy's startup configuration.
// Caddy does not accept --admin as a CLI flag; the admin listen address must
// be provided via a JSON config file. This provider writes a minimal bootstrap
// config and injects --config <path> into the process arguments.
// The admin port is allocated fresh each boot and must NOT be persisted in the database.
public partial class ProxyArgumentProvider
(
    ProxySettings settings,
    ILogger<ProxyArgumentProvider> logger
) : IProcessArgumentProvider
{
    private readonly ProxySettings _settings = settings
        ?? throw new ArgumentNullException(nameof(settings));

    private readonly ILogger<ProxyArgumentProvider> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    private string? _bootstrapConfigPath;

    public string? AugmentArguments(string appSlug, string? resolvedArguments)
    {
        if (!string.Equals(appSlug, "proxy", StringComparison.Ordinal))
        {
            return resolvedArguments;
        }

        // Strip stale flags from persisted arguments (handles databases seeded
        // before this fix where --admin or --config "" were baked into the override)
        var cleaned = resolvedArguments is not null
            ? AdminFlagPattern.Replace(
                ConfigFlagPattern.Replace(resolvedArguments, ""),
                "").Trim()
            : resolvedArguments;

        var bootstrapPath = WriteBootstrapConfig();

        var configArgument = $"""--config "{bootstrapPath}" """;

        var augmented = string.IsNullOrWhiteSpace(cleaned)
            ? configArgument
            : $"{cleaned} {configArgument}";

        _logger.LogInformation
        (
            "Injected bootstrap config into proxy arguments: admin on port {AdminPort}",
            _settings.AdminPort
        );

        return augmented;
    }

    private string WriteBootstrapConfig()
    {
        if (_bootstrapConfigPath is not null && File.Exists(_bootstrapConfigPath))
        {
            return _bootstrapConfigPath;
        }

        var adminListen = string.Format
        (
            CultureInfo.InvariantCulture,
            "localhost:{0}",
            _settings.AdminPort
        );

        var config = new JsonObject
        {
            ["admin"] = new JsonObject
            {
                ["listen"] = adminListen
            }
        };

        var tempDirectory = Path.Combine(Path.GetTempPath(), "collabhost");

        Directory.CreateDirectory(tempDirectory);

        _bootstrapConfigPath = Path.Combine(tempDirectory, "caddy-bootstrap.json");

        File.WriteAllText(_bootstrapConfigPath, config.ToJsonString());

        return _bootstrapConfigPath;
    }

    // Matches --admin followed by a host:port value (e.g., --admin localhost:12345)
    [GeneratedRegex(@"--admin\s+\S+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AdminFlagPattern { get; }

    // Matches --config followed by a quoted or unquoted path (e.g., --config "" or --config /path/to/file)
    [GeneratedRegex("""--config\s+("[^"]*"|\S+)""", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ConfigFlagPattern { get; }
}
