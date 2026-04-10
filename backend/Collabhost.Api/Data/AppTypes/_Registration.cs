namespace Collabhost.Api.Data.AppTypes;

public static class TypeStoreRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddTypeStore()
        {
            services.AddSingleton<TypeStore>();
            return services;
        }
    }
}
