using Collabhost.Api.Proxy;

using Microsoft.Extensions.Configuration;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Proxy;

public class ProxyRegistrationTests
{
    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().Build();

    private static IConfiguration ConfigWithProxy(params (string key, string value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection
            (
                entries.ToDictionary
                (
                    e => $"Proxy:{e.key}",
                    e => (string?)e.value,
                    StringComparer.Ordinal
                )
            )
            .Build();

    // --- COLLABHOST_PROXY_BASE_DOMAIN ---

    [Fact]
    public void ResolveSettings_BaseDomainEnvVarSet_WinsOverConfig()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_PROXY_BASE_DOMAIN", "collabhost.lan");

        try
        {
            var config = ConfigWithProxy(("BaseDomain", "collab.internal"));

            var result = ProxyRegistration.ResolveSettings(config);

            result.BaseDomain.ShouldBe("collabhost.lan");
        }
        finally
        {
            Environment.SetEnvironmentVariable("COLLABHOST_PROXY_BASE_DOMAIN", null);
        }
    }

    [Fact]
    public void ResolveSettings_BaseDomainEnvVarUnset_FallsBackToConfig()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_PROXY_BASE_DOMAIN", null);

        var config = ConfigWithProxy(("BaseDomain", "custom.internal"));

        var result = ProxyRegistration.ResolveSettings(config);

        result.BaseDomain.ShouldBe("custom.internal");
    }

    [Fact]
    public void ResolveSettings_BaseDomainEnvVarUnsetConfigUnset_ReturnsHardcodedDefault()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_PROXY_BASE_DOMAIN", null);

        var result = ProxyRegistration.ResolveSettings(EmptyConfig());

        result.BaseDomain.ShouldBe("collab.internal");
    }

    [Fact]
    public void ResolveSettings_BaseDomainEnvVarWhitespace_FallsBackToConfig()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_PROXY_BASE_DOMAIN", "   ");

        try
        {
            var config = ConfigWithProxy(("BaseDomain", "custom.internal"));

            var result = ProxyRegistration.ResolveSettings(config);

            result.BaseDomain.ShouldBe("custom.internal");
        }
        finally
        {
            Environment.SetEnvironmentVariable("COLLABHOST_PROXY_BASE_DOMAIN", null);
        }
    }

    // --- COLLABHOST_PROXY_LISTEN_ADDRESS ---

    [Fact]
    public void ResolveSettings_ListenAddressEnvVarSet_WinsOverConfig()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_PROXY_LISTEN_ADDRESS", ":8443");

        try
        {
            var config = ConfigWithProxy(("ListenAddress", ":443"));

            var result = ProxyRegistration.ResolveSettings(config);

            result.ListenAddress.ShouldBe(":8443");
        }
        finally
        {
            Environment.SetEnvironmentVariable("COLLABHOST_PROXY_LISTEN_ADDRESS", null);
        }
    }

    [Fact]
    public void ResolveSettings_ListenAddressEnvVarUnset_FallsBackToConfig()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_PROXY_LISTEN_ADDRESS", null);

        var config = ConfigWithProxy(("ListenAddress", ":9443"));

        var result = ProxyRegistration.ResolveSettings(config);

        result.ListenAddress.ShouldBe(":9443");
    }

    [Fact]
    public void ResolveSettings_ListenAddressEnvVarUnsetConfigUnset_ReturnsHardcodedDefault()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_PROXY_LISTEN_ADDRESS", null);

        var result = ProxyRegistration.ResolveSettings(EmptyConfig());

        result.ListenAddress.ShouldBe(":443");
    }

    [Fact]
    public void ResolveSettings_ListenAddressEnvVarWhitespace_FallsBackToConfig()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_PROXY_LISTEN_ADDRESS", "   ");

        try
        {
            var config = ConfigWithProxy(("ListenAddress", ":9443"));

            var result = ProxyRegistration.ResolveSettings(config);

            result.ListenAddress.ShouldBe(":9443");
        }
        finally
        {
            Environment.SetEnvironmentVariable("COLLABHOST_PROXY_LISTEN_ADDRESS", null);
        }
    }

    // --- COLLABHOST_PROXY_CERT_LIFETIME ---

    [Fact]
    public void ResolveSettings_CertLifetimeEnvVarSet_WinsOverConfig()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_PROXY_CERT_LIFETIME", "720h");

        try
        {
            var config = ConfigWithProxy(("CertLifetime", "168h"));

            var result = ProxyRegistration.ResolveSettings(config);

            result.CertLifetime.ShouldBe("720h");
        }
        finally
        {
            Environment.SetEnvironmentVariable("COLLABHOST_PROXY_CERT_LIFETIME", null);
        }
    }

    [Fact]
    public void ResolveSettings_CertLifetimeEnvVarUnset_FallsBackToConfig()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_PROXY_CERT_LIFETIME", null);

        var config = ConfigWithProxy(("CertLifetime", "336h"));

        var result = ProxyRegistration.ResolveSettings(config);

        result.CertLifetime.ShouldBe("336h");
    }

    [Fact]
    public void ResolveSettings_CertLifetimeEnvVarUnsetConfigUnset_ReturnsHardcodedDefault()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_PROXY_CERT_LIFETIME", null);

        var result = ProxyRegistration.ResolveSettings(EmptyConfig());

        result.CertLifetime.ShouldBe("168h");
    }

    [Fact]
    public void ResolveSettings_CertLifetimeEnvVarWhitespace_FallsBackToConfig()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_PROXY_CERT_LIFETIME", "   ");

        try
        {
            var config = ConfigWithProxy(("CertLifetime", "336h"));

            var result = ProxyRegistration.ResolveSettings(config);

            result.CertLifetime.ShouldBe("336h");
        }
        finally
        {
            Environment.SetEnvironmentVariable("COLLABHOST_PROXY_CERT_LIFETIME", null);
        }
    }

    // --- COLLABHOST_PROXY_SELF_PORT ---

    [Fact]
    public void ResolveSettings_SelfPortEnvVarSet_WinsOverConfig()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_PROXY_SELF_PORT", "9000");

        try
        {
            var config = ConfigWithProxy(("SelfPort", "58400"));

            var result = ProxyRegistration.ResolveSettings(config);

            result.SelfPort.ShouldBe(9000);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COLLABHOST_PROXY_SELF_PORT", null);
        }
    }

    [Fact]
    public void ResolveSettings_SelfPortEnvVarUnset_FallsBackToConfig()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_PROXY_SELF_PORT", null);

        var config = ConfigWithProxy(("SelfPort", "7000"));

        var result = ProxyRegistration.ResolveSettings(config);

        result.SelfPort.ShouldBe(7000);
    }

    [Fact]
    public void ResolveSettings_SelfPortEnvVarUnsetConfigUnset_ReturnsHardcodedDefault()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_PROXY_SELF_PORT", null);

        var result = ProxyRegistration.ResolveSettings(EmptyConfig());

        result.SelfPort.ShouldBe(58400);
    }

    [Fact]
    public void ResolveSettings_SelfPortEnvVarInvalid_FallsBackToConfig()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_PROXY_SELF_PORT", "not-a-number");

        try
        {
            var config = ConfigWithProxy(("SelfPort", "58400"));

            var result = ProxyRegistration.ResolveSettings(config);

            result.SelfPort.ShouldBe(58400);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COLLABHOST_PROXY_SELF_PORT", null);
        }
    }

    [Fact]
    public void ResolveSettings_SelfPortEnvVarOutOfRange_FallsBackToConfig()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_PROXY_SELF_PORT", "99999");

        try
        {
            var config = ConfigWithProxy(("SelfPort", "58400"));

            var result = ProxyRegistration.ResolveSettings(config);

            result.SelfPort.ShouldBe(58400);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COLLABHOST_PROXY_SELF_PORT", null);
        }
    }
}
