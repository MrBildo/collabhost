namespace Collabhost.Api.Registry;

public static class RegistryRegistration
{
    public static IServiceCollection AddRegistry(this IServiceCollection services)
    {
        services.AddSingleton<AppStore>();
        return services;
    }
}
