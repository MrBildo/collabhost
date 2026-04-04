namespace Collabhost.Api.Capabilities;

public static class CapabilityRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddCapabilities()
        {
            services.AddSingleton<CapabilityStore>();
            return services;
        }
    }
}
