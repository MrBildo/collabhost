using Collabhost.Api.Portal;

using Microsoft.Extensions.Configuration;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Portal;

public class PortalRegistrationTests
{
    private const string _envVar = "COLLABHOST_PORTAL_SUBDOMAIN";

    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().Build();

    private static IConfiguration ConfigWithSubdomain(string value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection
            (
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Portal:Subdomain"] = value
                }
            )
            .Build();

    [Fact]
    public void ResolveSettings_NoEnvNoConfig_ReturnsHardcodedDefault()
    {
        Environment.SetEnvironmentVariable(_envVar, null);

        var result = PortalRegistration.ResolveSettings(EmptyConfig());

        result.Subdomain.ShouldBe("collabhost");
    }

    [Fact]
    public void ResolveSettings_AppsettingOnly_ReturnsAppsettingValue()
    {
        Environment.SetEnvironmentVariable(_envVar, null);

        var result = PortalRegistration.ResolveSettings(ConfigWithSubdomain("portal"));

        result.Subdomain.ShouldBe("portal");
    }

    [Fact]
    public void ResolveSettings_EnvWins()
    {
        Environment.SetEnvironmentVariable(_envVar, "from-env");

        try
        {
            var result = PortalRegistration.ResolveSettings(ConfigWithSubdomain("from-config"));

            result.Subdomain.ShouldBe("from-env");
        }
        finally
        {
            Environment.SetEnvironmentVariable(_envVar, null);
        }
    }

    [Fact]
    public void ResolveSettings_WhitespaceEnv_FallsThroughToAppsetting()
    {
        Environment.SetEnvironmentVariable(_envVar, "   ");

        try
        {
            var result = PortalRegistration.ResolveSettings(ConfigWithSubdomain("portal"));

            result.Subdomain.ShouldBe("portal");
        }
        finally
        {
            Environment.SetEnvironmentVariable(_envVar, null);
        }
    }

    [Fact]
    public void ResolveSettings_WhitespaceEnv_NoAppsetting_ReturnsDefault()
    {
        Environment.SetEnvironmentVariable(_envVar, "   ");

        try
        {
            var result = PortalRegistration.ResolveSettings(EmptyConfig());

            result.Subdomain.ShouldBe("collabhost");
        }
        finally
        {
            Environment.SetEnvironmentVariable(_envVar, null);
        }
    }

    [Fact]
    public void ResolveSettings_EmptyEnv_FallsThroughToAppsetting()
    {
        Environment.SetEnvironmentVariable(_envVar, "");

        try
        {
            var result = PortalRegistration.ResolveSettings(ConfigWithSubdomain("portal"));

            result.Subdomain.ShouldBe("portal");
        }
        finally
        {
            Environment.SetEnvironmentVariable(_envVar, null);
        }
    }
}
