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
    private static readonly HostingSettings _hosting = new() { ListenPort = 58400 };
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

    private static ProxySettings MakeSettings(string listenAddress) =>
        new()
        {
            BaseDomain = "collab.internal",
            BinaryPath = "caddy",
            ListenAddress = listenAddress,
            CertLifetime = "168h",
            AdminPort = 2019
        };

    private static JsonArray GetListenArray(JsonObject config)
    {
        var listen = config["apps"]!["http"]!["servers"]!["srv0"]!["listen"]!.AsArray();

        return listen;
    }
}
