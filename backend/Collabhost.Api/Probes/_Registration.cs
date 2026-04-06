namespace Collabhost.Api.Probes;

public static class ProbeRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddProbes()
        {
            services.AddSingleton<ProbeService>();
            services.AddHostedService<ProbeStartupService>();
            return services;
        }
    }
}
