using System.Globalization;

namespace Collabhost.Api.Auth;

public static class AuthExtensions
{
    public static IServiceCollection AddCollabhostAuth
    (
        this IServiceCollection services,
        IConfiguration configuration,
        ILogger logger
    )
    {
        services.Configure<AuthSettings>(configuration.GetSection("Auth"));

        var generatedKey = Ulid.NewUlid().ToString(null, CultureInfo.InvariantCulture);

        services.PostConfigure<AuthSettings>
        (
            s =>
            {
                if (s.AdminKey is not null)
                {
                    return;
                }

                s.AdminKey = generatedKey;

                logger.LogWarning
                (
                    "No Auth:AdminKey configured. Generated temporary key: {AdminKey}",
                    generatedKey
                );
            }
        );

        return services;
    }

    public static IApplicationBuilder UseCollabhostAuth(this IApplicationBuilder app)
    {
        app.UseMiddleware<ApiKeyAuthMiddleware>();
        return app;
    }
}
