namespace Collabhost.Api.Platform;

public static class PlatformRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddPlatform()
        {
            services.AddSingleton<IBootVersionWriter, BootVersionWriter>();

            return services;
        }
    }
}
