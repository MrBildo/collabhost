using Collabhost.Api.Supervisor;

using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Collabhost.Api.Proxy;

public static class ProxyRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddProxy(IConfiguration configuration, string dataDirectory)
        {
            var proxySettings = ResolveSettings(configuration);

            // AdminPort is NOT allocated here anymore. It is claimed at supervisor
            // start (via ProxyAdminPortInitializer) after every pinned port has been
            // reserved, so the admin port is drawn from -- and excluded from -- the
            // same pool managed apps use. Allocating it at DI-registration time used a
            // static, reservation-blind allocator that ran before any pin existed; that
            // hole is what the #373 completeness contract (item 2) closes.
            services.AddSingleton(proxySettings);

            services.AddSingleton<IReservedPortInitializer, ProxyAdminPortInitializer>();

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
            //
            // The admin port is no longer known at DI time (it is claimed at supervisor
            // start, after pin hydration), so the base address is resolved lazily on the
            // client's first request rather than baked into the HttpClient here. By the
            // time any admin call runs the proxy process has started and the port is set.
            //
            // Timeout is pinned to 10s instead of the BCL 100s default: the admin API is
            // localhost and millisecond-scale, and a wedged admin call (e.g. a LoadConfig
            // during route sync) on the 100s default would block the sequential sync
            // pipeline for the full window. 10s sits above VerifyCaddyReadyAsync's 1s
            // per-attempt CTS so it never preempts the readiness retry loop, while a hung
            // sync call now fails fast. The client has no IHttpClientFactory pooling/recycle
            // (it bypasses the factory above), so the timeout is set on the instance directly.
            services.AddSingleton<ICaddyClient>
            (
                provider => new CaddyClient
                (
                    new HttpClient { Timeout = TimeSpan.FromSeconds(10) },
                    provider.GetRequiredService<ProxySettings>(),
                    provider.GetRequiredService<ILogger<CaddyClient>>()
                )
            );

            services.AddSingleton<ProxyAppSeeder>();

            // dataDirectory is the resolve-once effectiveDataDir from Program.cs -- the same
            // owner-scoped, writable root the DB and per-app data dirs use. The proxy
            // bootstrap config (PRX-03) writes a per-boot-unique file under {dataDir}/proxy/
            // here instead of the former shared world-writable {TEMP}/collabhost path.
            services.AddSingleton<IProcessArgumentProvider>
            (
                provider => new ProxyArgumentProvider
                (
                    provider.GetRequiredService<ProxySettings>(),
                    dataDirectory,
                    provider.GetRequiredService<ILogger<ProxyArgumentProvider>>()
                )
            );

            services.AddSingleton<IProcessEnvironmentProvider, ProxyEnvironmentProvider>();

            // Card #258: ProxyManager's probe loop accepts an injected TimeProvider so
            // tests can run on virtual time. Production wires the system clock here.
            // TryAdd so a host that has already registered TimeProvider (or a test
            // harness that overrides it) wins.
            services.TryAddSingleton(TimeProvider.System);

            services.AddSingleton<ProxyManager>();
            services.AddHostedService(provider => provider.GetRequiredService<ProxyManager>());

            // The reload-proxy operation (code-structure-conventions §8/§9 -- a concrete
            // IOperation<,> registered in its OWNING subsystem's _Registration.cs, explicitly,
            // no assembly-scan). Scoped to match the per-request ICurrentUser the Operation<,>
            // base actor-stamps with, exactly as the Registry lifecycle operations register.
            services.AddScoped<ReloadProxyOperation>();

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
        // COLLABHOST_PROXY_BINARY_PATH env var is read directly by CaddyResolver and does not
        // need threading through settings.
        var binaryPath = section["BinaryPath"];

        var envListenAddress = Environment.GetEnvironmentVariable("COLLABHOST_PROXY_LISTEN_ADDRESS");

        // Default ":80,:443" — Caddy auto-emits an HTTP->HTTPS redirect server on :80
        // when both ports are listed, so http:// typos land at the right place. Operators
        // who can't grant privileged-port binds can override with COLLABHOST_PROXY_LISTEN_ADDRESS=":8080,:8443"
        // or by editing appsettings.json. Card #217.
        var listenAddress = !string.IsNullOrWhiteSpace(envListenAddress)
            ? envListenAddress
            : section["ListenAddress"] ?? ":80,:443";

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

        // StoragePath is operator-opt-in. Unset (env + config + default all empty) leaves
        // ProxyConfigurationBuilder emitting no storage block, which preserves Caddy's
        // built-in default behavior bit-for-bit -- the additive contract for v1.0.x
        // installs that haven't set the value. Card #230 phase 1.
        var envStoragePath = Environment.GetEnvironmentVariable("COLLABHOST_PROXY_STORAGE_PATH");

        var storagePath = !string.IsNullOrWhiteSpace(envStoragePath)
            ? envStoragePath
            : section["StoragePath"];

        return new ProxySettings
        {
            BaseDomain = baseDomain,
            BinaryPath = binaryPath,
            ListenAddress = listenAddress,
            CertLifetime = certLifetime,
            DnsProvider = string.IsNullOrWhiteSpace(dnsProvider) ? null : dnsProvider,
            DnsApiTokenEnvVar = dnsApiTokenEnvVar,
            StoragePath = string.IsNullOrWhiteSpace(storagePath) ? null : storagePath
        };
    }
}
