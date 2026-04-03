namespace Collabhost.Api.Supervisor;

public static class SupervisorRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSupervisor()
        {
            services.AddSingleton<PortAllocator>();
            services.AddSingleton<IManagedProcessRunner, WindowsProcessRunner>();
            services.AddSingleton<ProcessSupervisor>();
            services.AddHostedService(provider => provider.GetRequiredService<ProcessSupervisor>());

            return services;
        }
    }
}
