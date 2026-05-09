using System.Globalization;
using System.Net;

namespace Collabhost.Api.Platform;

// Composes the Kestrel UseUrls() value from HostingSettings.ListenAddress + ListenPort.
// Pure function so the IPv6-bracketing rule has unit-test coverage independent of the
// startup pipeline. Card #218.
//
// IPv6 raw addresses must be wrapped in brackets in URLs ("http://[::1]:58400"); the
// IPAddress.TryParse path detects them deterministically. Hostnames ("localhost",
// "host.lan") and IPv4 addresses ("127.0.0.1", "0.0.0.0") render bare.
public static class HostingUrlBuilder
{
    public static string Build(string listenAddress, int listenPort)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(listenAddress);

        var trimmed = listenAddress.Trim();

        var renderedHost = IPAddress.TryParse(trimmed, out var parsed)
            && parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                ? string.Create(CultureInfo.InvariantCulture, $"[{parsed}]")
                : trimmed;

        return string.Format
        (
            CultureInfo.InvariantCulture,
            "http://{0}:{1}",
            renderedHost,
            listenPort
        );
    }
}
