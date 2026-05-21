namespace Collabhost.Api.StaticSite;

public static class StaticSiteRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddStaticSite()
        {
            services.AddSingleton<RuntimeConfigFileWriter>();
            return services;
        }
    }
}
