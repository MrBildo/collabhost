using System.Globalization;

using Collabhost.Api.Supervisor;

namespace Collabhost.Api.Proxy;

// Injects the dynamic admin port into Caddy's startup configuration.
// Caddy does not accept --admin as a CLI flag; the admin listen address must
// be provided via a JSON config file. This provider writes a minimal bootstrap
// config and injects --config <path> into the process arguments.
// The admin port is allocated fresh each boot and must NOT be persisted in the database.
//
// The bootstrap config is written under the per-install data dir (the owner-scoped,
// writable effectiveDataDir from Program.cs) with a per-boot-unique filename, NOT the
// former fixed {TEMP}/collabhost/caddy-bootstrap.json (PRX-03). The old shared-temp path
// was a multi-instance collision point and a /tmp-squat hazard: where PrivateTmp isn't
// set, a predictable world-writable path hands an attacker the Caddy admin listen address.
public partial class ProxyArgumentProvider
(
    ProxySettings settings,
    string dataDirectory,
    ILogger<ProxyArgumentProvider> logger
) : IProcessArgumentProvider
{
    private const string _proxySubdirectory = "proxy";

    private const string _bootstrapFilePrefix = "caddy-bootstrap-";

    private readonly ProxySettings _settings = settings
        ?? throw new ArgumentNullException(nameof(settings));

    private readonly string _dataDirectory = !string.IsNullOrWhiteSpace(dataDirectory)
        ? dataDirectory
        : throw new ArgumentException("Data directory must be a non-empty path.", nameof(dataDirectory));

    private readonly ILogger<ProxyArgumentProvider> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    // Per-boot token, fixed for the provider's (process') lifetime. Distinct across
    // concurrent installs and across boots; stable across a Caddy restart within one boot
    // so the restarted process re-reads the same bootstrap file.
    private readonly string _bootToken = Guid.NewGuid().ToString("N");

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

        var proxyDirectory = Path.Combine(_dataDirectory, _proxySubdirectory);

        Directory.CreateDirectory(proxyDirectory);

        // Clear any bootstrap files left by prior boots. The per-boot-unique name means a
        // fixed-name overwrite no longer reclaims the slot, so without this sweep the dir
        // would accumulate one stale file per boot.
        SweepStaleBootstrapFiles(proxyDirectory);

        _bootstrapConfigPath = Path.Combine(proxyDirectory, $"{_bootstrapFilePrefix}{_bootToken}.json");

        File.WriteAllText(_bootstrapConfigPath, config.ToJsonString());

        return _bootstrapConfigPath;
    }

    private void SweepStaleBootstrapFiles(string proxyDirectory)
    {
        try
        {
            foreach (var stale in Directory.EnumerateFiles(proxyDirectory, $"{_bootstrapFilePrefix}*.json"))
            {
                File.Delete(stale);
            }
        }
        catch (IOException ex)
        {
            // Best-effort cleanup -- a stale-file delete failure must not block the boot.
            _logger.LogWarning(ex, "Failed to clear a stale proxy bootstrap config under {ProxyDirectory}.", proxyDirectory);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Failed to clear a stale proxy bootstrap config under {ProxyDirectory}.", proxyDirectory);
        }
    }

    // Matches --admin followed by a host:port value (e.g., --admin localhost:12345)
    [GeneratedRegex(@"--admin\s+\S+", RegexOptions.None, 1000)]
    private static partial Regex AdminFlagPattern { get; }

    // Matches --config followed by a quoted or unquoted path (e.g., --config "" or --config /path/to/file)
    [GeneratedRegex("""--config\s+("[^"]*"|\S+)""", RegexOptions.None, 1000)]
    private static partial Regex ConfigFlagPattern { get; }
}
