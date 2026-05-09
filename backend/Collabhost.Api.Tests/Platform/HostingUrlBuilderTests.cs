using Collabhost.Api.Platform;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Platform;

// Unit coverage for the HostingSettings -> Kestrel UseUrls() string. The IPv6-bracket rule is
// the only non-trivial branch; everything else is "render '<addr>:<port>' verbatim." Card #218.
public class HostingUrlBuilderTests
{
    [Fact]
    public void Build_LocalhostHostname_RendersBare() =>
        HostingUrlBuilder.Build("localhost", 58400)
            .ShouldBe("http://localhost:58400");

    [Fact]
    public void Build_IPv4Loopback_RendersBare() =>
        HostingUrlBuilder.Build("127.0.0.1", 58400)
            .ShouldBe("http://127.0.0.1:58400");

    [Fact]
    public void Build_IPv4Wildcard_RendersBare() =>
        // 0.0.0.0 binds every interface -- this is the canonical headless-server posture.
        HostingUrlBuilder.Build("0.0.0.0", 58400)
            .ShouldBe("http://0.0.0.0:58400");

    [Fact]
    public void Build_IPv4SpecificNic_RendersBare() =>
        HostingUrlBuilder.Build("192.168.1.10", 58400)
            .ShouldBe("http://192.168.1.10:58400");

    [Fact]
    public void Build_HostnameWithSubdomain_RendersBare() =>
        HostingUrlBuilder.Build("api.lan", 58400)
            .ShouldBe("http://api.lan:58400");

    [Fact]
    public void Build_IPv6Loopback_RendersBracketed() =>
        // RFC 3986: a literal IPv6 address in a URL authority must be wrapped in square
        // brackets. Without the brackets Kestrel's URL parser interprets the colons as
        // host:port:port and rejects.
        HostingUrlBuilder.Build("::1", 58400)
            .ShouldBe("http://[::1]:58400");

    [Fact]
    public void Build_IPv6Wildcard_RendersBracketed() =>
        HostingUrlBuilder.Build("::", 58400)
            .ShouldBe("http://[::]:58400");

    [Fact]
    public void Build_IPv6FullForm_RendersBracketed() =>
        HostingUrlBuilder.Build("fe80::1", 58400)
            .ShouldBe("http://[fe80::1]:58400");

    [Fact]
    public void Build_TrimsSurroundingWhitespace() =>
        HostingUrlBuilder.Build("  localhost ", 58400)
            .ShouldBe("http://localhost:58400");

    [Fact]
    public void Build_NullAddress_Throws() =>
        Should.Throw<ArgumentException>(() => HostingUrlBuilder.Build(null!, 58400));

    [Fact]
    public void Build_EmptyAddress_Throws() =>
        Should.Throw<ArgumentException>(() => HostingUrlBuilder.Build(string.Empty, 58400));

    [Fact]
    public void Build_WhitespaceAddress_Throws() =>
        Should.Throw<ArgumentException>(() => HostingUrlBuilder.Build("   ", 58400));
}
