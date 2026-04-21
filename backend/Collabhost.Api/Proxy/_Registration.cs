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

            services.AddHttpClient<ICaddyClient, CaddyClient>
            (
                client => client.BaseAddress = new Uri(adminBaseAddress)
            );

            services.AddSingleton<ProxyAppSeeder>();
            services.AddSingleton<IProcessArgumentProvider, ProxyArgumentProvider>();
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

        // Each setting follows §12.3 precedence: env var > appsettings.json > hardcoded default
        var baseDomain = Environment.GetEnvironmentVariable("COLLABHOST_PROXY_BASE_DOMAIN")
            ?? section["BaseDomain"]
            ?? "collab.internal";

        var binaryPath = section["BinaryPath"]
            ?? "caddy";

        var listenAddress = Environment.GetEnvironmentVariable("COLLABHOST_PROXY_LISTEN_ADDRESS")
            ?? section["ListenAddress"]
            ?? ":443";

        var certLifetime = Environment.GetEnvironmentVariable("COLLABHOST_PROXY_CERT_LIFETIME")
            ?? section["CertLifetime"]
            ?? "168h";

        var selfPort = ResolveSelfPort(section);

        return new ProxySettings
        {
            BaseDomain = baseDomain,
            BinaryPath = binaryPath,
            ListenAddress = listenAddress,
            CertLifetime = certLifetime,
            SelfPort = selfPort
        };
    }

    private static int ResolveSelfPort(IConfigurationSection section)
    {
        const int defaultSelfPort = 58400;

        var envValue = Environment.GetEnvironmentVariable("COLLABHOST_PROXY_SELF_PORT");

        return !string.IsNullOrWhiteSpace(envValue)
            && int.TryParse(envValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fromEnv)
            && fromEnv is >= 1 and <= 65535
            ? fromEnv
            : section.GetValue<int?>("SelfPort") ?? defaultSelfPort;
    }
}
