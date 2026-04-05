using System.Globalization;

using Collabhost.Api.Supervisor;

namespace Collabhost.Api.Proxy;

// Injects the dynamic admin port into Caddy's arguments at process start time.
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

    public string? AugmentArguments(string appSlug, string? resolvedArguments)
    {
        if (!string.Equals(appSlug, "proxy", StringComparison.Ordinal))
        {
            return resolvedArguments;
        }

        // Strip any stale --admin flag from persisted arguments (handles databases
        // seeded before the dynamic port fix where --admin was baked into the override)
        var cleaned = resolvedArguments is not null
            ? AdminFlagPattern.Replace(resolvedArguments, "").Trim()
            : resolvedArguments;

        var adminArgument = string.Format
        (
            CultureInfo.InvariantCulture,
            "--admin localhost:{0}",
            _settings.AdminPort
        );

        var augmented = string.IsNullOrWhiteSpace(cleaned)
            ? adminArgument
            : $"{cleaned} {adminArgument}";

        _logger.LogInformation
        (
            "Injected dynamic admin port into proxy arguments: {AdminArgument}",
            adminArgument
        );

        return augmented;
    }

    // Matches --admin followed by a host:port value (e.g., --admin localhost:12345)
    [GeneratedRegex(@"--admin\s+\S+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AdminFlagPattern { get; }
}
