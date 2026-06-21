using System.Text.RegularExpressions;

using Collabhost.Api.Proxy;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Proxy;

public class ProxyArgumentProviderTests : IDisposable
{
    // Each test gets an isolated, real, writable data dir -- the per-install owner-scoped
    // root the bootstrap config now lives under (PRX-03), standing in for effectiveDataDir.
    // Deliberately NOT prefixed "collabhost" so the squat-path assertion below can't pass by
    // coincidence of a shared string prefix (the squat check is segment-aware regardless).
    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), "ch-proxy-tests", Guid.NewGuid().ToString("N"));

    // Matches the per-boot-unique bootstrap filename shape under the proxy subdir.
    private static readonly Regex _bootstrapPathPattern =
        new(@"caddy-bootstrap-[0-9a-fA-F]+\.json", RegexOptions.None, TimeSpan.FromSeconds(1));

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
        result.ShouldMatch(_bootstrapPathPattern.ToString());

        var bootstrapPath = ExtractConfigPath(result);
        File.Exists(bootstrapPath).ShouldBeTrue();

        var bootstrapContent = File.ReadAllText(bootstrapPath);
        bootstrapContent.ShouldContain("localhost:54321");
    }

    // PRX-03: the bootstrap config lives under the per-install data dir, NOT the shared
    // world-writable temp directory. The old fixed {TEMP}/collabhost path was a multi-instance
    // collision point and a /tmp-squat hazard (handing an attacker the Caddy admin address
    // where PrivateTmp isn't set).
    [Fact]
    public void AugmentArguments_ProxySlug_WritesUnderDataDirectoryNotTemp()
    {
        var provider = CreateProvider(adminPort: 54321);

        var result = provider.AugmentArguments("proxy", "run");

        var bootstrapPath = ExtractConfigPath(result);

        Path.GetFullPath(bootstrapPath)
            .ShouldStartWith(Path.GetFullPath(_dataDirectory), Case.Insensitive);

        // Not parked under the former shared {TEMP}/collabhost/ squat path. Compared as a path
        // segment (trailing separator) so a sibling dir like "collabhost-x" can't false-pass.
        var sharedTempSquat = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "collabhost"))
            + Path.DirectorySeparatorChar;
        Path.GetFullPath(bootstrapPath).ShouldNotStartWith(sharedTempSquat, Case.Insensitive);
    }

    // PRX-03: the bootstrap filename is per-boot unique, so two concurrent installs (two
    // provider instances, here standing in for two boots) never collide on one shared file.
    [Fact]
    public void AugmentArguments_TwoBoots_ProduceDistinctBootstrapFilenames()
    {
        var bootA = CreateProvider(adminPort: 54321);
        var bootB = CreateProvider(adminPort: 54321);

        var pathA = ExtractConfigPath(bootA.AugmentArguments("proxy", "run")!);
        var pathB = ExtractConfigPath(bootB.AugmentArguments("proxy", "run")!);

        Path.GetFileName(pathA).ShouldNotBe(Path.GetFileName(pathB));
    }

    // PRX-03: a single provider (one boot) reuses one bootstrap file across repeated calls
    // -- a Caddy restart within the boot must re-read the same config, so the per-boot token
    // is fixed for the provider's lifetime, not regenerated per call.
    [Fact]
    public void AugmentArguments_SameBoot_ReusesOneBootstrapFile()
    {
        var provider = CreateProvider(adminPort: 54321);

        var first = ExtractConfigPath(provider.AugmentArguments("proxy", "run")!);
        var second = ExtractConfigPath(provider.AugmentArguments("proxy", "run")!);

        second.ShouldBe(first);
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
        result.ShouldMatch(_bootstrapPathPattern.ToString());
    }

    [Fact]
    public void AugmentArguments_ProxySlugNullArguments_ReturnsConfigOnly()
    {
        var provider = CreateProvider(adminPort: 54321);

        var result = provider.AugmentArguments("proxy", null);

        result.ShouldNotBeNull();
        result.ShouldContain("--config");
        result.ShouldMatch(_bootstrapPathPattern.ToString());
    }

    [Fact]
    public void AugmentArguments_ProxySlugEmptyArguments_ReturnsConfigOnly()
    {
        var provider = CreateProvider(adminPort: 54321);

        var result = provider.AugmentArguments("proxy", "");

        result.ShouldNotBeNull();
        result.ShouldContain("--config");
        result.ShouldMatch(_bootstrapPathPattern.ToString());
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

        var result = provider.AugmentArguments("proxy", "run");

        var bootstrapPath = ExtractConfigPath(result!);

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
        result.ShouldMatch(_bootstrapPathPattern.ToString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    // Pulls the quoted path out of the injected --config "..." argument.
    private static string ExtractConfigPath(string? augmentedArguments)
    {
        augmentedArguments.ShouldNotBeNull();

        var match = Regex.Match
        (
            augmentedArguments,
            "--config\\s+\"(?<path>[^\"]*)\"",
            RegexOptions.ExplicitCapture,
            TimeSpan.FromSeconds(1)
        );

        match.Success.ShouldBeTrue($"No --config \"path\" found in: {augmentedArguments}");

        return match.Groups["path"].Value;
    }

    private ProxyArgumentProvider CreateProvider(int adminPort) =>
        new
        (
            new ProxySettings
            {
                BaseDomain = "collab.internal",
                BinaryPath = "caddy",
                ListenAddress = ":443",
                CertLifetime = "168h",
                AdminPort = adminPort
            },
            _dataDirectory,
            NullLogger<ProxyArgumentProvider>.Instance
        );
}
