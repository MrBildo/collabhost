using Collabhost.Api.Supervisor;

namespace Collabhost.Api.Proxy;

// Plumbs the DNS provider's API token from Collabhost's host process env into
// Caddy's child process env at spawn time. Token never touches the database --
// it lives only in the host process env (set by the operator via systemd unit
// override, .env file, or shell wrapper).
//
// Caddy's ACME issuer references the token via {env.<NAME>} placeholder syntax
// in the JSON config; this provider is what makes the env var visible to the
// Caddy child so the placeholder resolves at issue time.
//
// Self-filtering by appSlug == "proxy" follows the established pattern from
// ProxyArgumentProvider.
public class ProxyEnvironmentProvider
(
    ProxySettings settings,
    ILogger<ProxyEnvironmentProvider> logger
) : IProcessEnvironmentProvider
{
    private readonly ProxySettings _settings = settings
        ?? throw new ArgumentNullException(nameof(settings));

    private readonly ILogger<ProxyEnvironmentProvider> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    public IReadOnlyDictionary<string, string> ContributeEnvironment(string appSlug)
    {
        if (!string.Equals(appSlug, "proxy", StringComparison.Ordinal))
        {
            return _emptyEnvironment;
        }

        // Internal-CA branch needs no token. Skip silently when the operator
        // hasn't opted into a DNS provider.
        if (string.IsNullOrWhiteSpace(_settings.DnsProvider))
        {
            return _emptyEnvironment;
        }

        var tokenEnvVar = _settings.DnsApiTokenEnvVar;

        if (string.IsNullOrWhiteSpace(tokenEnvVar))
        {
            _logger.LogWarning
            (
                "DnsProvider is set to '{DnsProvider}' but DnsApiTokenEnvVar is empty -- ACME DNS-01 challenge will fail at issue time",
                _settings.DnsProvider
            );

            return _emptyEnvironment;
        }

        var tokenValue = Environment.GetEnvironmentVariable(tokenEnvVar);

        if (string.IsNullOrWhiteSpace(tokenValue))
        {
            _logger.LogWarning
            (
                "DnsProvider is set to '{DnsProvider}' but env var '{TokenEnvVar}' is not set on the host process -- ACME DNS-01 challenge will fail at issue time",
                _settings.DnsProvider,
                tokenEnvVar
            );

            return _emptyEnvironment;
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [tokenEnvVar] = tokenValue
        };
    }

    private static readonly IReadOnlyDictionary<string, string> _emptyEnvironment =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
