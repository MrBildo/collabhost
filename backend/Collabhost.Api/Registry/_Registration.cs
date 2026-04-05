using Collabhost.Api.Supervisor;

namespace Collabhost.Api.Registry;

public static class RegistryRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddRegistry()
        {
            services.AddSingleton<AppStore>();
            return services;
        }
    }

    extension(IEndpointRouteBuilder routes)
    {
        public IEndpointRouteBuilder MapRegistryEndpoints()
        {
            AppEndpoints.Map(routes);
            AppTypeEndpoints.Map(routes);
            LogStreamEndpoints.Map(routes);
            return routes;
        }
    }
}
