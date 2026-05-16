using Collabhost.Api.Supervisor;

namespace Collabhost.Api.Registry;

public static class RegistryRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddRegistry(string dataDirectory)
        {
            services.AddSingleton<AppStore>();

            // Per-app writable data path surfaced on the registration / get_app
            // contract (#326 / #322 decision E1). dataDirectory is the
            // resolve-once effectiveDataDir from Program.cs -- the same value
            // AddSupervisor receives -- NOT re-derived here, so all install
            // scopes (system / user / Windows) stay correct. Independent of the
            // removed Axis-B cwd-redirect accommodation by construction.
            services.AddSingleton(new AppDataPathResolver(dataDirectory));

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
