namespace Collabhost.Api.Capabilities;

public static class CapabilityRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddCapabilities(IConfiguration configuration)
        {
            services.AddSingleton<CapabilityStore>();

            var externalTargetSettings = ResolveExternalTargetSettings(configuration);
            services.AddSingleton(externalTargetSettings);

            return services;
        }
    }

    // env > config > false. Card #348, D3. AllowPublicHosts is a deliberate
    // operator opt-in -- the platform default is private-only validation on
    // external-target hosts. Whitespace env-var falls through to config so an
    // accidentally-blank wrapper script value does not mask the appsettings.json
    // entry, matching the HostingSettings / ProxySettings env-precedence shape.
    internal static ExternalTargetSettings ResolveExternalTargetSettings(IConfiguration configuration)
    {
        var section = configuration.GetSection(ExternalTargetSettings.SectionName);

        var envValue = Environment.GetEnvironmentVariable("COLLABHOST_EXTERNAL_TARGET_ALLOW_PUBLIC_HOSTS");

        var allowPublicHosts = !string.IsNullOrWhiteSpace(envValue) && bool.TryParse(envValue, out var fromEnv)
            ? fromEnv
            : section.GetValue<bool?>("AllowPublicHosts") ?? false;

        return new ExternalTargetSettings
        {
            AllowPublicHosts = allowPublicHosts
        };
    }
}
