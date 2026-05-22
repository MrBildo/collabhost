using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Collabhost.Api.Probes;

public static class ProbeRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddProbes()
        {
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<ProbeService>();
            services.AddHostedService<ProbeStartupService>();
            services.AddHostedService<ProbePeriodicService>();
            return services;
        }
    }
}
