using Collabhost.Api.Events;

namespace Collabhost.Api.Data.AppTypes;

public static class TypeStoreRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddTypeStore(IConfiguration configuration)
        {
            var settings = configuration.GetSection(TypeStoreSettings.SectionName).Get<TypeStoreSettings>()
                ?? new TypeStoreSettings { UserTypesDirectory = "UserTypes" };

            services.AddSingleton(settings);
            services.AddSingleton<TypeStore>();
            services.AddSingleton<IEventBus<TypeStoreReloadedEvent>, EventBus<TypeStoreReloadedEvent>>();

            return services;
        }
    }
}
