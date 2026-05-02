using System.Globalization;

using Collabhost.Api.Supervisor;

namespace Collabhost.Api.Proxy;

public static class ProxyRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddProxy(IConfiguration configuration)
        {
            var proxySettings = ResolveSettings(configuration);

            proxySettings.AdminPort = PortAllocator.AllocatePort();

            services.AddSingleton(proxySettings);

            var adminBaseAddress = string.Format
            (
                CultureInfo.InvariantCulture,
                "http://localhost:{0}",
                proxySettings.AdminPort
            );

            // CaddyClient talks to a localhost admin API and ProxyManager.VerifyCaddyReadyAsync
            // already runs its own retry loop (5s budget, 1s per-attempt, 200ms delay). The
            // standard resilience pipeline applied by ConfigureHttpClientDefaults (Collabhost.
            // ServiceDefaults) wraps every IHttpClientFactory-produced client with Polly retry +
            // circuit breaker + its own timeouts, which turns the probe's per-attempt
            // cancellation into a connection-acquisition wait inside the resilience handler
            // and produces the "probe disappears after one attempt" cold-boot bug (#153
            // Phase 2 regression observed on PR #94).
            //
            // Registering the client manually -- bypassing AddHttpClient -- means
            // ConfigureHttpClientDefaults does not touch it. Caddy admin is localhost and
            // long-lived; a plain HttpClient is the right shape here.
            services.AddSingleton<ICaddyClient>
            (
                provider => new CaddyClient
                (
                    new HttpClient { BaseAddress = new Uri(adminBaseAddress) },
                    provider.GetRequiredService<ILogger<CaddyClient>>()
                )
            );

            services.AddSingleton<ProxyAppSeeder>();
            services.AddSingleton<IProcessArgumentProvider, ProxyArgumentProvider>();
            services.AddSingleton<IProcessEnvironmentProvider, ProxyEnvironmentProvider>();
            services.AddSingleton<ProxyManager>();
            services.AddHostedService(provider => provider.GetRequiredService<ProxyManager>());

            return services;
        }
    }

    extension(IEndpointRouteBuilder routes)
    {
        public IEndpointRouteBuilder MapProxyEndpoints()
        {
            ProxyEndpoints.Map(routes);
            return routes;
        }
    }

    // Internal visibility for unit tests
    internal static ProxySettings ResolveSettings(IConfiguration configuration)
    {
        var section = configuration.GetSection(ProxySettings.SectionName);

        // Each setting follows env var > appsettings.json > hardcoded default precedence.
        // IsNullOrWhiteSpace is used for env-var checks so that accidentally whitespace-only
        // values (e.g. trailing space in a shell script) fall through to config rather than winning.
        var envBaseDomain = Environment.GetEnvironmentVariable("COLLABHOST_PROXY_BASE_DOMAIN");

        var baseDomain = !string.IsNullOrWhiteSpace(envBaseDomain)
            ? envBaseDomain
            : section["BaseDomain"] ?? "collab.internal";

        // BinaryPath is optional. CaddyResolver returns null when both env var and this
        // setting are unconfigured, and the proxy subsystem boots disabled. The
        // COLLABHOST_CADDY_PATH env var is read directly by CaddyResolver and does not
        // need threading through settings.
        var binaryPath = section["BinaryPath"];

        var envListenAddress = Environment.GetEnvironmentVariable("COLLABHOST_PROXY_LISTEN_ADDRESS");

        var listenAddress = !string.IsNullOrWhiteSpace(envListenAddress)
            ? envListenAddress
            : section["ListenAddress"] ?? ":443";

        var envCertLifetime = Environment.GetEnvironmentVariable("COLLABHOST_PROXY_CERT_LIFETIME");

        var certLifetime = !string.IsNullOrWhiteSpace(envCertLifetime)
            ? envCertLifetime
            : section["CertLifetime"] ?? "168h";

        var envDnsProvider = Environment.GetEnvironmentVariable("COLLABHOST_PROXY_DNS_PROVIDER");

        var dnsProvider = !string.IsNullOrWhiteSpace(envDnsProvider)
            ? envDnsProvider
            : section["DnsProvider"];

        var envDnsApiTokenEnvVar = Environment.GetEnvironmentVariable("COLLABHOST_PROXY_DNS_API_TOKEN_ENV_VAR");

        var dnsApiTokenEnvVar = !string.IsNullOrWhiteSpace(envDnsApiTokenEnvVar)
            ? envDnsApiTokenEnvVar
            : section["DnsApiTokenEnvVar"] ?? "CLOUDFLARE_API_TOKEN";

        return new ProxySettings
        {
            BaseDomain = baseDomain,
            BinaryPath = binaryPath,
            ListenAddress = listenAddress,
            CertLifetime = certLifetime,
            DnsProvider = string.IsNullOrWhiteSpace(dnsProvider) ? null : dnsProvider,
            DnsApiTokenEnvVar = dnsApiTokenEnvVar
        };
    }
}
