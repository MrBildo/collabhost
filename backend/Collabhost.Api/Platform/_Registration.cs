namespace Collabhost.Api.Platform;

public static class PlatformRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddPlatform()
        {
            services.AddSingleton<IBootVersionWriter, BootVersionWriter>();
            services.AddSingleton<IApplicationStartTime, ApplicationStartTime>();

            // SIGHUP -> TypeStore.ReloadAsync. Linux-only behavior; the service still starts
            // on Windows / macOS but no-ops in StartAsync. See SighupReloadService for rationale.
            services.AddHostedService<SighupReloadService>();

            return services;
        }
    }
}
