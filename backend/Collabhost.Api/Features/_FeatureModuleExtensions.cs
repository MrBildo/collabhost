using System.Reflection;

using Collabhost.Api.Features;

namespace Microsoft.Extensions.DependencyInjection;

public static class FeatureModuleServiceExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddFeatureModules(Assembly assembly)
        {
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
