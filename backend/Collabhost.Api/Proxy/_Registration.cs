using System.Globalization;

using Collabhost.Api.Supervisor;

namespace Collabhost.Api.Proxy;

public static class ProxyRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddProxy(IConfiguration configuration)
        {
            var proxySettings = configuration
                .GetSection(ProxySettings.SectionName)
                .Get<ProxySettings>()
                ?? throw new InvalidOperationException
                (
                    $"Missing '{ProxySettings.SectionName}' configuration section."
                );

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
}
