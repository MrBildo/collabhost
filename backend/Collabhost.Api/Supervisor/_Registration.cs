using Collabhost.Api.Supervisor.Containment;
using Collabhost.Api.Supervisor.Resources;

using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Collabhost.Api.Supervisor;

public static class SupervisorRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSupervisor(string dataDirectory)
        {
            services.AddSingleton<PortAllocator>();

            // Per-app single-file bundle-extraction dir lives under the resolved
            // data root (same root install-system.sh L60-62 grants for Collabhost's
            // own binary, already inside the unit's ReadWritePaths). dataDirectory
            // is the canonical resolve-once value from Program.cs -- not re-derived
            // here, so all install scopes (system / user / Windows) stay correct. (#313.)
            services.AddSingleton(provider => new HostedAppBundleDirectory
            (
                dataDirectory,
                provider.GetRequiredService<ILogger<HostedAppBundleDirectory>>()
            ));

            // Per-app sandbox-writable runtime working directory, the cwd
            // analogue of the bundle dir above (#316 / Axis B). Same data root
            // (already inside the unit's ReadWritePaths), same resolve-once
            // dataDirectory from Program.cs -- NOT re-derived here, so all
            // install scopes stay correct.
            services.AddSingleton(provider => new HostedAppWorkingDirectory
            (
                dataDirectory,
                provider.GetRequiredService<ILogger<HostedAppWorkingDirectory>>()
            ));

            if (OperatingSystem.IsWindows())
            {
                services.AddSingleton<IManagedProcessRunner, WindowsProcessRunner>();
                services.AddSingleton<IProcessContainment, WindowsJobObjectContainment>();
                services.AddSingleton<IProcessResourceSampler, WindowsProcessResourceSampler>();
            }
            else if (OperatingSystem.IsLinux())
            {
                services.AddSingleton<IManagedProcessRunner, LinuxProcessRunner>();
                services.AddSingleton<IProcessContainment, LinuxContainment>();
                services.AddSingleton<IProcessResourceSampler, LinuxProcessResourceSampler>();
            }
            else
            {
                // Degraded mode: start, output capture, and hard kill only.
                // No graceful shutdown (CloseMainWindow returns false for console apps).
                // No orphan protection (NullContainment). NullProcessResourceSampler
                // returns null for every Sample call, leaving AppDetail.resources null.
                services.AddSingleton<IManagedProcessRunner, FallbackProcessRunner>();
                services.AddSingleton<IProcessContainment, NullContainment>();
                services.AddSingleton<IProcessResourceSampler, NullProcessResourceSampler>();
            }

            services.AddSingleton<ProcessSupervisor>();
            services.AddHostedService(provider => provider.GetRequiredService<ProcessSupervisor>());

            // Resource sampling. The cache is consumed by AppEndpoints (read-only via
            // IProcessResourceCache); the sampler service writes to the cache. TimeProvider
            // is shared with the proxy subsystem (already TryAdd-registered there); TryAdd
            // here protects against AddSupervisor being called after AddProxy.
            services.AddSingleton<ProcessResourceCache>();
            services.AddSingleton<IProcessResourceCache>(provider =>
                provider.GetRequiredService<ProcessResourceCache>());
            services.TryAddSingleton(TimeProvider.System);
            services.AddHostedService<ProcessResourceSamplerService>();

            return services;
        }
    }
}
