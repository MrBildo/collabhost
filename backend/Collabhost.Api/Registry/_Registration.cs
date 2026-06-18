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

            // Operation spine (code-structure-conventions §8: central-explicit DI, no
            // assembly-scan -- each operation enumerated here, in its owning subsystem's
            // registration). Scoped because the Operation<,> base injects the scoped
            // ICurrentUser for actor stamping. Both surfaces (REST endpoint, MCP tool)
            // inject the concrete operation directly and call it -- no dispatcher.
            services.AddScoped<StartAppOperation>();
            services.AddScoped<StopAppOperation>();
            services.AddScoped<RestartAppOperation>();
            services.AddScoped<KillAppOperation>();
            services.AddScoped<UpdateSettingsOperation>();

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
