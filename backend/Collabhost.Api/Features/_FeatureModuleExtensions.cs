using System.Reflection;

using Collabhost.Api.Features;

namespace Microsoft.Extensions.DependencyInjection;

public static class FeatureModuleServiceExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddFeatureModules(Assembly assembly)
        {
            // Auto-register all query handler classes found in the Features namespace
            AddFeatureQueryHandlers(services, assembly);

            // Discover and register IFeatureModule instances for endpoint mapping
            var moduleTypes = assembly.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false })
                .Where(t => t.IsAssignableTo(typeof(IFeatureModule)));

            foreach (var moduleType in moduleTypes)
            {
                var module = (IFeatureModule)Activator.CreateInstance(moduleType)!;
                services.AddSingleton<IFeatureModule>(module);
            }

            return services;
        }
    }

    private static void AddFeatureQueryHandlers(IServiceCollection services, Assembly assembly)
    {
        var handlerTypes = assembly.GetTypes()
            .Where
            (
                t => t is { IsClass: true, IsAbstract: false, IsNested: false }
                    && t.Name.EndsWith("QueryHandler", StringComparison.Ordinal)
                    && (t.Namespace?.Contains(".Features") ?? false)
            );

        foreach (var handlerType in handlerTypes)
        {
            services.AddScoped(handlerType);
        }
    }
}

public static class FeatureModuleAppExtensions
{
    extension(WebApplication app)
    {
        public WebApplication MapFeatureModuleEndpoints()
        {
            foreach (var module in app.Services.GetServices<IFeatureModule>())
            {
                module.MapEndpoints(app);
            }

            return app;
        }
    }
}
