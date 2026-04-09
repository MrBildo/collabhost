using Collabhost.Api.Supervisor.Containment;

namespace Collabhost.Api.Supervisor;

public static class SupervisorRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSupervisor()
        {
            services.AddSingleton<PortAllocator>();

            if (OperatingSystem.IsWindows())
            {
                services.AddSingleton<IManagedProcessRunner, WindowsProcessRunner>();
                services.AddSingleton<IProcessContainment, WindowsJobObjectContainment>();
            }
            else if (OperatingSystem.IsLinux())
            {
                services.AddSingleton<IManagedProcessRunner, LinuxProcessRunner>();
                services.AddSingleton<IProcessContainment, LinuxContainment>();
            }
            else
            {
                // Degraded mode: start, output capture, and hard kill only.
                // No graceful shutdown (CloseMainWindow returns false for console apps).
                // No orphan protection (NullContainment).
                services.AddSingleton<IManagedProcessRunner, FallbackProcessRunner>();
                services.AddSingleton<IProcessContainment, NullContainment>();
            }

            services.AddSingleton<ProcessSupervisor>();
            services.AddHostedService(provider => provider.GetRequiredService<ProcessSupervisor>());

            return services;
        }
    }
}
