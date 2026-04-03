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

            services.AddSingleton(proxySettings);

            services.AddHttpClient<ICaddyClient, CaddyClient>
            (
                client => client.BaseAddress = new Uri(proxySettings.AdminApiUrl)
            );

            services.AddSingleton<ProxyAppSeeder>();
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
