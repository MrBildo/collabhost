using Collabhost.Api.Proxy;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Proxy;

public class ProxyArgumentProviderTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "collabhost");

    [Fact]
    public void AugmentArguments_NonProxySlug_ReturnsUnchanged()
    {
        var provider = CreateProvider(adminPort: 54321);

        var result = provider.AugmentArguments("my-web-app", "--some-flag");

        result.ShouldBe("--some-flag");
    }

    [Fact]
    public void AugmentArguments_ProxySlug_InjectsBootstrapConfig()
    {
        var provider = CreateProvider(adminPort: 54321);

        var result = provider.AugmentArguments("proxy", "run");

        result.ShouldNotBeNull();
        result.ShouldContain("--config");
        result.ShouldContain("caddy-bootstrap.json");

        // Verify the bootstrap file contains the correct admin port
        var bootstrapPath = Path.Combine(_tempDirectory, "caddy-bootstrap.json");
        File.Exists(bootstrapPath).ShouldBeTrue();

        var bootstrapContent = File.ReadAllText(bootstrapPath);
        bootstrapContent.ShouldContain("localhost:54321");
    }

    [Fact]
    public void AugmentArguments_ProxySlug_StripsStaleAdminFlag()
    {
        var provider = CreateProvider(adminPort: 54321);

        var result = provider.AugmentArguments("proxy", "run --admin localhost:99999");

        result.ShouldNotBeNull();
        result.ShouldNotContain("--admin");
        result.ShouldNotContain("99999");
        result.ShouldContain("--config");
    }

    [Fact]
    public void AugmentArguments_ProxySlug_StripsStaleConfigFlag()
    {
        var provider = CreateProvider(adminPort: 54321);

        var result = provider.AugmentArguments("proxy", """run --config "" """);

        result.ShouldNotBeNull();
        result.ShouldNotContain("""--config "" """);
        result.ShouldContain("caddy-bootstrap.json");
    }

    [Fact]
    public void AugmentArguments_ProxySlugNullArguments_ReturnsConfigOnly()
    {
        var provider = CreateProvider(adminPort: 54321);

        var result = provider.AugmentArguments("proxy", null);

        result.ShouldNotBeNull();
        result.ShouldContain("--config");
        result.ShouldContain("caddy-bootstrap.json");
    }

    [Fact]
    public void AugmentArguments_ProxySlugEmptyArguments_ReturnsConfigOnly()
    {
        var provider = CreateProvider(adminPort: 54321);

        var result = provider.AugmentArguments("proxy", "");

        result.ShouldNotBeNull();
        result.ShouldContain("--config");
        result.ShouldContain("caddy-bootstrap.json");
    }

    [Fact]
    public void AugmentArguments_ProxySlug_PreservesRunSubcommand()
    {
        var provider = CreateProvider(adminPort: 12345);

        var result = provider.AugmentArguments("proxy", "run");

        result.ShouldNotBeNull();
        result.ShouldStartWith("run");
        result.ShouldContain("--config");
    }

    [Fact]
    public void AugmentArguments_ProxySlug_BootstrapConfigIsValidJson()
    {
        var provider = CreateProvider(adminPort: 54321);

        provider.AugmentArguments("proxy", "run");

        var bootstrapPath = Path.Combine(_tempDirectory, "caddy-bootstrap.json");

        var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(bootstrapPath));
        var adminListen = document.RootElement
            .GetProperty("admin")
            .GetProperty("listen")
            .GetString();

        adminListen.ShouldBe("localhost:54321");
    }

    [Fact]
    public void AugmentArguments_ProxySlug_StripsStaleAdminAndConfig()
    {
        var provider = CreateProvider(adminPort: 54321);

        var result = provider.AugmentArguments("proxy", """run --config "" --admin localhost:99999""");

        result.ShouldNotBeNull();
        result.ShouldNotContain("--admin");
        result.ShouldNotContain("99999");
        result.ShouldStartWith("run");
        result.ShouldContain("caddy-bootstrap.json");
    }

    public void Dispose()
    {
        var bootstrapPath = Path.Combine(_tempDirectory, "caddy-bootstrap.json");

        if (File.Exists(bootstrapPath))
        {
            File.Delete(bootstrapPath);
        }

        GC.SuppressFinalize(this);
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
