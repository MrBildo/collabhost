using Collabhost.Api.Proxy;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Proxy;

public class ProxyArgumentProviderTests
{
    [Fact]
    public void AugmentArguments_NonProxySlug_ReturnsUnchanged()
    {
        var provider = CreateProvider(adminPort: 54321);

        var result = provider.AugmentArguments("my-web-app", "--some-flag");

        result.ShouldBe("--some-flag");
    }

    [Fact]
    public void AugmentArguments_ProxySlug_InjectsAdminPort()
    {
        var provider = CreateProvider(adminPort: 54321);

        var result = provider.AugmentArguments("proxy", """run --config "" """);

        result.ShouldNotBeNull();
        result.ShouldContain("--admin localhost:54321");
    }

    [Fact]
    public void AugmentArguments_ProxySlug_StripsStaleAdminFlag()
    {
        var provider = CreateProvider(adminPort: 54321);

        var result = provider.AugmentArguments("proxy", """run --config "" --admin localhost:99999""");

        result.ShouldNotBeNull();
        result.ShouldContain("--admin localhost:54321");
        result.ShouldNotContain("99999");
    }

    [Fact]
    public void AugmentArguments_ProxySlugNullArguments_ReturnsAdminOnly()
    {
        var provider = CreateProvider(adminPort: 54321);

        var result = provider.AugmentArguments("proxy", null);

        result.ShouldBe("--admin localhost:54321");
    }

    [Fact]
    public void AugmentArguments_ProxySlugEmptyArguments_ReturnsAdminOnly()
    {
        var provider = CreateProvider(adminPort: 54321);

        var result = provider.AugmentArguments("proxy", "");

        result.ShouldBe("--admin localhost:54321");
    }

    [Fact]
    public void AugmentArguments_ProxySlug_PreservesOtherArguments()
    {
        var provider = CreateProvider(adminPort: 12345);

        var result = provider.AugmentArguments("proxy", """run --config "" """);

        result.ShouldNotBeNull();
        result.ShouldStartWith("run --config");
        result.ShouldEndWith("--admin localhost:12345");
    }

    private static ProxyArgumentProvider CreateProvider(int adminPort) =>
        new
        (
            new ProxySettings
            {
                BaseDomain = "collab.internal",
                BinaryPath = "caddy",
                ListenAddress = ":443",
                CertLifetime = "168h",
                SelfPort = 58400,
                AdminPort = adminPort
            },
            NullLogger<ProxyArgumentProvider>.Instance
        );
}
