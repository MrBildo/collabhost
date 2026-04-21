using Collabhost.Api.Proxy;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Proxy;

// CaddyResolver owns the full precedence chain as of #153 Phase 2.
// Binary-path resolution tests live in CaddyResolverTests; this file retains only
// high-level guards for ProxyAppSeeder itself. See CaddyResolverTests for the full suite.
public class ProxyAppSeederTests
{
    [Fact]
    public void ResolveBinaryPathSetting_BareName_ResolvesFromPath()
    {
        // 'where' / 'which' exists on every supported platform's PATH.
        var result = CaddyResolver.ResolveBinaryPathSetting(OperatingSystem.IsWindows() ? "where" : "sh");

        result.ShouldNotBeNull();
    }

    [Fact]
    public void ResolveBinaryPathSetting_BareNameNotOnPath_ReturnsNull() =>
        CaddyResolver.ResolveBinaryPathSetting("nonexistent-binary-12345").ShouldBeNull();
}
