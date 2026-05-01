namespace Collabhost.Api.Portal;

public static class PortalRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddPortal(IConfiguration configuration)
        {
            var settings = ResolveSettings(configuration);
            services.AddSingleton(settings);
            return services;
        }
    }

    extension(IApplicationBuilder app)
    {
        public IApplicationBuilder UsePortal()
        {
            // Order: UseDefaultFiles rewrites GET / to /index.html before UseStaticFiles
            // runs. UseStaticFiles short-circuits on disk hits. UsePortalSpaFallback
            // handles unmatched non-API HTML GETs. All three run in the middleware phase
            // BEFORE auth so the SPA shell reaches the browser unauthenticated; auth runs
            // at API-call time. See PortalSpaFallbackMiddleware for the predicate.
            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.UseMiddleware<PortalSpaFallbackMiddleware>();
            return app;
        }
    }

    // Internal visibility for unit tests
    internal static PortalSettings ResolveSettings(IConfiguration configuration)
    {
        var section = configuration.GetSection(PortalSettings.SectionName);

        // env > config > hardcoded default. Whitespace env value falls through so an
        // accidentally-blank wrapper script value does not mask the appsettings.json entry.
        // Pattern matches ProxyRegistration.ResolveSettings for the COLLABHOST_* family.
        var envSubdomain = Environment.GetEnvironmentVariable("COLLABHOST_PORTAL_SUBDOMAIN");

        var subdomain = !string.IsNullOrWhiteSpace(envSubdomain)
            ? envSubdomain
            : section["Subdomain"] ?? "collabhost";

        return new PortalSettings { Subdomain = subdomain };
    }
}
