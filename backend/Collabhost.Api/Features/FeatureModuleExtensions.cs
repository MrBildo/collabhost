using System.Reflection;

namespace Collabhost.Api.Features;

public static class FeatureModuleExtensions
{
    public static IServiceCollection AddFeatureModules(this IServiceCollection services, Assembly assembly)
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

    public static WebApplication MapFeatureModuleEndpoints(this WebApplication app)
    {
        foreach (var module in app.Services.GetServices<IFeatureModule>())
        {
            module.MapEndpoints(app);
        }

        return app;
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
