using System.Reflection;

namespace Collabhost.Api.Features;

public static class FeatureModuleExtensions
{
    public static IServiceCollection AddFeatureModules(this IServiceCollection services, Assembly assembly)
    {
        var moduleTypes = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.IsAssignableTo(typeof(IFeatureModule)));

        foreach (var moduleType in moduleTypes)
        {
            var module = (IFeatureModule)Activator.CreateInstance(moduleType)!;
            module.RegisterServices(services);
            services.AddSingleton(typeof(IFeatureModule), module);
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
}
