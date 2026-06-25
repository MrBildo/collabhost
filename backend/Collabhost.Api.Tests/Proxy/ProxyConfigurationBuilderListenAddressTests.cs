using System.Text.Json.Nodes;

using Collabhost.Api.Platform;
using Collabhost.Api.Portal;
using Collabhost.Api.Proxy;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Proxy;

// Listen-address parsing for srv0.listen. Default is ":80,:443" so Caddy auto-emits
// the HTTP->HTTPS redirect server on :80; operators with custom appsettings can opt
// out. Card #217.
public class ProxyConfigurationBuilderListenAddressTests
{
    private static readonly HostingSettings _hosting = new() { ListenAddress = "localhost", ListenPort = 58400 };
    private static readonly PortalSettings _portal = new() { Subdomain = "collabhost" };

    [Fact]
    public void Build_ListenAddressIsSinglePort_ProducesSingleEntryArray()
    {
        var settings = MakeSettings(":443");

        var config = ProxyConfigurationBuilder.Build([], settings, _hosting, _portal);

        var listen = GetListenArray(config);

        listen.Count.ShouldBe(1);
        listen[0]!.GetValue<string>().ShouldBe(":443");
    }

    [Fact]
    public void Build_ListenAddressIsCommaSeparated_ProducesMultiEntryArray()
    {
        var settings = MakeSettings(":80,:443");

        var config = ProxyConfigurationBuilder.Build([], settings, _hosting, _portal);

        var listen = GetListenArray(config);

        listen.Count.ShouldBe(2);
        listen[0]!.GetValue<string>().ShouldBe(":80");
        listen[1]!.GetValue<string>().ShouldBe(":443");
    }

    [Fact]
    public void Build_ListenAddressHasWhitespaceAroundCommas_TrimsEachEntry()
    {
        var settings = MakeSettings(":80, :443 , :8080");

        var config = ProxyConfigurationBuilder.Build([], settings, _hosting, _portal);

        var listen = GetListenArray(config);

        listen.Count.ShouldBe(3);
        listen[0]!.GetValue<string>().ShouldBe(":80");
        listen[1]!.GetValue<string>().ShouldBe(":443");
        listen[2]!.GetValue<string>().ShouldBe(":8080");
    }

    [Fact]
    public void Build_ListenAddressHasEmptyEntries_DropsThem()
    {
        var settings = MakeSettings(":80,,:443,");

        var config = ProxyConfigurationBuilder.Build([], settings, _hosting, _portal);

        var listen = GetListenArray(config);

        listen.Count.ShouldBe(2);
        listen[0]!.GetValue<string>().ShouldBe(":80");
        listen[1]!.GetValue<string>().ShouldBe(":443");
    }

    [Fact]
    public void Build_ListenAddressIsHighPorts_StillWorks()
    {
        // Operators on hosts where they cannot grant cap_net_bind_service set
        // ":8080,:8443" or similar.
        var settings = MakeSettings(":8080,:8443");

        var config = ProxyConfigurationBuilder.Build([], settings, _hosting, _portal);

        var listen = GetListenArray(config);

        listen.Count.ShouldBe(2);
        listen[0]!.GetValue<string>().ShouldBe(":8080");
        listen[1]!.GetValue<string>().ShouldBe(":8443");
    }

    // --- #444: the auto-HTTPS redirect server must not bind :80 when the
    // operator is fully on alt-ports. Caddy spins up an HTTP->HTTPS redirect on
    // http_port (:80) for any TLS listener; on a host that cannot bind :80
    // (rootless, second instance, restricted) the whole config fails to load.
    // The fix suppresses the redirect (automatic_https.disable_redirects) ONLY
    // when the listen set carries no standard port (:80 or :443) -- so the
    // standard-port happy-path (#217 :443->:80 promotion, and the :80,:443
    // default) stays byte-identical and prod is untouched.

    [Fact]
    public void Build_ListenAddressIsAltPortsPair_DisablesAutoHttpsRedirects()
    {
        var settings = MakeSettings(":8080,:8443");

        var config = ProxyConfigurationBuilder.Build([], settings, _hosting, _portal);

        GetAutoHttpsDisableRedirects(config).ShouldBeTrue();
    }

    [Fact]
    public void Build_ListenAddressIsSingleAltTlsPort_DisablesAutoHttpsRedirects()
    {
        // The card #444 repro: a lone ":8443" still drives Caddy to bind :80 for
        // the redirect server, taking the config down on a host without
        // CAP_NET_BIND_SERVICE.
        var settings = MakeSettings(":8443");

        var config = ProxyConfigurationBuilder.Build([], settings, _hosting, _portal);

        GetAutoHttpsDisableRedirects(config).ShouldBeTrue();
    }

    [Fact]
    public void Build_ListenAddressIsDefaultStandardPorts_LeavesAutoHttpsRedirectsEnabled()
    {
        // The :80,:443 default (appsettings.json) keeps the redirect -- the host
        // binds :80 itself. Emitting no automatic_https leaves Caddy's default
        // behavior intact; prod runs on this.
        var settings = MakeSettings(":80,:443");

        var config = ProxyConfigurationBuilder.Build([], settings, _hosting, _portal);

        GetServer(config)["automatic_https"].ShouldBeNull();
    }

    [Fact]
    public void Build_ListenAddressIsBare443_LeavesAutoHttpsRedirectsEnabled()
    {
        // The #217 happy-path Bill named as a hard constraint: a bare ":443" is
        // promoted by Caddy into an HTTP->HTTPS-on-:80 redirect. :443 is a
        // standard port, so no automatic_https override is emitted -- unchanged.
        var settings = MakeSettings(":443");

        var config = ProxyConfigurationBuilder.Build([], settings, _hosting, _portal);

        GetServer(config)["automatic_https"].ShouldBeNull();
    }

    private static ProxySettings MakeSettings(string listenAddress) =>
        new()
        {
            BaseDomain = "collab.internal",
            BinaryPath = "caddy",
            ListenAddress = listenAddress,
            CertLifetime = "168h",
            AdminPort = 2019
        };

    private static JsonObject GetServer(JsonObject config) =>
        config["apps"]!["http"]!["servers"]!["srv0"]!.AsObject();

    private static JsonArray GetListenArray(JsonObject config) =>
        GetServer(config)["listen"]!.AsArray();

    private static bool GetAutoHttpsDisableRedirects(JsonObject config) =>
        GetServer(config)["automatic_https"]!["disable_redirects"]!.GetValue<bool>();
}
