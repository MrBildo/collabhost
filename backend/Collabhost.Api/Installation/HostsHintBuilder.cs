using System.Globalization;

namespace Collabhost.Api.Installation;

// Shared helper that composes the post-registration "run --update-hosts" hint surfaced on the
// REST and MCP register-app responses. Centralized so the prose stays in one place. Card #345.
public static class HostsHintBuilder
{
    public static string Compose(string hostname)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostname);

        var verb = OperatingSystem.IsWindows()
            ? "Start-Process collabhost -ArgumentList '--update-hosts' -Verb RunAs"
            : "sudo collabhost --update-hosts";

        return string.Format
        (
            CultureInfo.InvariantCulture,
            "To resolve '{0}' from a browser on this host, run: {1}",
            hostname,
            verb
        );
    }
}
