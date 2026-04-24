namespace Collabhost.Api.Authorization;

public static class AuthorizationRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddCollabhostAuthorization(IConfiguration configuration)
        {
            services.Configure<AuthorizationSettings>
            (
                configuration.GetSection(AuthorizationSettings.SectionName)
            );

            // COLLABHOST_ADMIN_KEY (env) wins over Auth:AdminKey (config). The flat
            // COLLABHOST_{NAME} convention means the default env provider does NOT auto-bind
            // to Auth:AdminKey, so we read the env var explicitly here and override the bound
            // settings. Same env-over-config pattern used for COLLABHOST_DATA_PATH /
            // COLLABHOST_USER_TYPES_PATH.
            //
            // Whitespace-only values are treated as unset so a blank env var in a startup
            // wrapper falls through to config.
            var envAdminKey = Environment.GetEnvironmentVariable("COLLABHOST_ADMIN_KEY");
            var effectiveEnvKey = string.IsNullOrWhiteSpace(envAdminKey) ? null : envAdminKey;

            if (effectiveEnvKey is not null)
            {
                services.PostConfigure<AuthorizationSettings>
                (
                    settings => settings.AdminKey = effectiveEnvKey
                );
            }

            // UserSeedService is invoked inline as phase (8) of the production startup sequence.
            // Not a hosted service -- the inline-before-RunAsync call site lets seeding failures
            // halt startup with an explicit exit code via StartupStderr, matching the
            // ProxyAppSeeder pattern in phase (7).
            services.AddSingleton<UserSeedService>();

            services.AddSingleton<UserStore>();
            services.AddSingleton<AuthKeyResolver>();
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

    extension(IEndpointRouteBuilder routes)
    {
        public IEndpointRouteBuilder MapUserEndpoints()
        {
            UserEndpoints.Map(routes);
            return routes;
        }
    }
}
