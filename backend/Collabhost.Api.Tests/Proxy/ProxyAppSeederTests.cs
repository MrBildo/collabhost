using Collabhost.Api.Proxy;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Proxy;

// CaddyResolver owns the full precedence chain as of #153 Phase 2.
// Binary-path resolution tests live in CaddyResolverTests; this file retains only
// high-level guards for ProxyAppSeeder itself. See CaddyResolverTests for the full suite.
public class ProxyAppSeederTests
{
    // Card #196: bare-name PATH walking was removed. A name without a directory
    // separator now fails File.Exists and returns null. Operators routing to a
    // system Caddy must use COLLABHOST_CADDY_PATH with an absolute path.
    [Fact]
    public void ResolveBinaryPathSetting_BareName_ReturnsNull() =>
        CaddyResolver.ResolveBinaryPathSetting(OperatingSystem.IsWindows() ? "where" : "sh")
            .ShouldBeNull();

    [Fact]
    public void ResolveBinaryPathSetting_NonexistentPath_ReturnsNull() =>
        CaddyResolver.ResolveBinaryPathSetting("nonexistent-binary-12345").ShouldBeNull();
}
