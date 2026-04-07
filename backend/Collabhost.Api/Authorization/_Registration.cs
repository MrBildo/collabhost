using System.Globalization;

namespace Collabhost.Api.Authorization;

public static class AuthorizationRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddCollabhostAuthorization
        (
            IConfiguration configuration,
            ILogger logger
        )
        {
            services.Configure<AuthorizationSettings>
            (
                configuration.GetSection(AuthorizationSettings.SectionName)
            );

            var generatedKey = Ulid.NewUlid().ToString(null, CultureInfo.InvariantCulture);

            services.PostConfigure<AuthorizationSettings>
            (
                settings =>
                {
                    if (settings.AdminKey is not null)
                    {
                        return;
                    }

                    settings.AdminKey = generatedKey;

                    logger.LogWarning
                    (
                        "No Auth:AdminKey configured. Generated temporary key: {AdminKey}",
                        generatedKey
                    );
                }
            );

            services.AddHostedService<UserSeedService>();

            services.AddSingleton<UserStore>();
            services.AddScoped<CurrentUser>();
            services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());

            return services;
        }
    }

    extension(IApplicationBuilder app)
    {
        public IApplicationBuilder UseCollabhostAuthorization()
        {
            app.UseMiddleware<AuthorizationMiddleware>();
            return app;
        }
    }
}
