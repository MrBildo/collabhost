using Collabhost.Api.Supervisor.Containment;

namespace Collabhost.Api.Supervisor;

public static class SupervisorRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSupervisor()
        {
            services.AddSingleton<PortAllocator>();
            services.AddSingleton<IManagedProcessRunner, WindowsProcessRunner>();

            if (OperatingSystem.IsWindows())
            {
                services.AddSingleton<IProcessContainment, WindowsJobObjectContainment>();
            }
            else
            {
                services.AddSingleton<IProcessContainment, NullContainment>();
            }

            services.AddSingleton<ProcessSupervisor>();
            services.AddHostedService(provider => provider.GetRequiredService<ProcessSupervisor>());

            return services;
        }
    }
}
