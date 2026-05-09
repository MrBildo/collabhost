using System.Globalization;

namespace Collabhost.Api.Platform;

public static class HostingRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddHosting(IConfiguration configuration)
        {
            var settings = ResolveSettings(configuration);
            services.AddSingleton(settings);
            return services;
        }
    }

    // Internal visibility for unit tests
    internal static HostingSettings ResolveSettings(IConfiguration configuration)
    {
        var section = configuration.GetSection(HostingSettings.SectionName);
        var listenAddress = ResolveListenAddress(section);
        var listenPort = ResolveListenPort(section);

        return new HostingSettings
        {
            ListenAddress = listenAddress,
            ListenPort = listenPort
        };
    }

    private static string ResolveListenAddress(IConfigurationSection section)
    {
        const string defaultListenAddress = "localhost";

        // env > config > hardcoded default. Whitespace env var falls through to config so
        // an accidentally-blank wrapper script value does not mask the appsettings.json
        // entry. Same blank-is-unset shape as ListenPort. Card #218.
        var envValue = Environment.GetEnvironmentVariable("COLLABHOST_HOSTING_LISTEN_ADDRESS");

        if (!string.IsNullOrWhiteSpace(envValue))
        {
            return envValue.Trim();
        }

        var configValue = section.GetValue<string?>("ListenAddress");

        return string.IsNullOrWhiteSpace(configValue)
            ? defaultListenAddress
            : configValue.Trim();
    }

    private static int ResolveListenPort(IConfigurationSection section)
    {
        const int defaultListenPort = 58400;

        // env > config > hardcoded default. Whitespace env var falls through to config so that
        // an accidentally-blank wrapper script value does not mask the appsettings.json entry.
        var envValue = Environment.GetEnvironmentVariable("COLLABHOST_HOSTING_LISTEN_PORT");

        return !string.IsNullOrWhiteSpace(envValue)
            && int.TryParse(envValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fromEnv)
            && fromEnv is >= 1 and <= 65535
            ? fromEnv
            : section.GetValue<int?>("ListenPort") ?? defaultListenPort;
    }
}
