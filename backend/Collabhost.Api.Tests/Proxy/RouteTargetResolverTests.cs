using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Proxy;

// Unit coverage for the shared RouteTargetResolver (Card #435). The resolver is the one
// home for the upstream-target string the 4 route surfaces (REST App Detail + Routes, MCP
// get_app + list_routes) render. Each branch below was a literal duplicated across those
// four sites before the dedup; this suite pins the contract the parity test then proves
// the four surfaces honor.
public class RouteTargetResolverTests
{
    private static RoutingConfiguration Routing(ServeMode mode) =>
        new() { ServeMode = mode };

    private static ExternalTargetConfiguration External(string host, int port, string scheme) =>
        new() { Host = host, Port = port, Scheme = scheme };

    [Fact]
    public void ResolveTarget_FileServer_ReturnsFileServerLabel()
    {
        // Card #435 convergence (operator ruling B): every surface now reports the file-server
        // label as the single string "Static Files". App Detail + both MCP tools previously
        // emitted the raw Caddy handler name "file-server"; converging to "Static Files" honors
        // the vendor-abstraction rule (no Caddy handler name in operator-facing output) and the
        // Routes table already used this value.
        var target = RouteTargetResolver.ResolveTarget
        (
            Routing(ServeMode.FileServer),
            hasExternalTarget: false,
            externalTarget: null,
            processPort: null
        );

        target.ShouldBe("Static Files");
    }

    [Fact]
    public void ResolveTarget_ExternalTarget_Http_ReturnsSchemeHostPort()
    {
        var target = RouteTargetResolver.ResolveTarget
        (
            Routing(ServeMode.ReverseProxy),
            hasExternalTarget: true,
            External("192.168.1.50", 11235, "http"),
            processPort: null
        );

        target.ShouldBe("http://192.168.1.50:11235");
    }

    [Fact]
    public void ResolveTarget_ExternalTarget_Https_ReturnsSchemeHostPort()
    {
        var target = RouteTargetResolver.ResolveTarget
        (
            Routing(ServeMode.ReverseProxy),
            hasExternalTarget: true,
            External("upstream.local", 8443, "https"),
            processPort: null
        );

        target.ShouldBe("https://upstream.local:8443");
    }

    [Fact]
    public void ResolveTarget_ExternalTarget_NullConfig_ReturnsNotConfigured()
    {
        // hasExternalTarget true (the capability is bound) but the merged config is missing.
        var target = RouteTargetResolver.ResolveTarget
        (
            Routing(ServeMode.ReverseProxy),
            hasExternalTarget: true,
            externalTarget: null,
            processPort: null
        );

        target.ShouldBe("not-configured");
    }

    [Theory]
    [InlineData("", 11235)]
    [InlineData("   ", 11235)]
    [InlineData("host", 0)]
    [InlineData("host", -1)]
    public void ResolveTarget_ExternalTarget_BlankHostOrInvalidPort_ReturnsNotConfigured(string host, int port)
    {
        var target = RouteTargetResolver.ResolveTarget
        (
            Routing(ServeMode.ReverseProxy),
            hasExternalTarget: true,
            External(host, port, "http"),
            processPort: null
        );

        target.ShouldBe("not-configured");
    }

    [Fact]
    public void ResolveTarget_SupervisedProcess_PortAllocated_ReturnsLocalhostPort()
    {
        var target = RouteTargetResolver.ResolveTarget
        (
            Routing(ServeMode.ReverseProxy),
            hasExternalTarget: false,
            externalTarget: null,
            processPort: 5005
        );

        target.ShouldBe("localhost:5005");
    }

    [Fact]
    public void ResolveTarget_SupervisedProcess_NoPort_ReturnsNotRunning()
    {
        var target = RouteTargetResolver.ResolveTarget
        (
            Routing(ServeMode.ReverseProxy),
            hasExternalTarget: false,
            externalTarget: null,
            processPort: null
        );

        target.ShouldBe("not-running");
    }

    // An external-target app whose serve mode is somehow FileServer still resolves to the
    // file-server label -- ServeMode is the outer discriminator, exactly as every surface had it.
    [Fact]
    public void ResolveTarget_FileServer_IgnoresExternalTarget()
    {
        var target = RouteTargetResolver.ResolveTarget
        (
            Routing(ServeMode.FileServer),
            hasExternalTarget: true,
            External("host", 80, "http"),
            processPort: 5005
        );

        target.ShouldBe("Static Files");
    }
}
