using System.Globalization;

using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Collabhost.Api.Platform;

// Cross-validates Hosting:ListenPort (what Caddy dials for collabhost.collab.internal -> Kestrel)
// against the API's actual bound listen port. The two are independent declarations of the
// same number: Hosting:ListenPort lives in the Hosting section / COLLABHOST_HOSTING_LISTEN_PORT
// env var, while the listen port observed by Kestrel is resolved from urls / ASPNETCORE_URLS /
// launchSettings.json (dev) and falls through to Hosting:ListenPort only when none of those
// are set (see Program.cs).
//
// Under Aspire or `dotnet run`, ASPNETCORE_URLS wins for Kestrel and ListenPort still governs
// the Caddy dial -- if an operator pins one and forgets the other, the dashboard's self-route
// silently 502s with no obvious cause. Card #165 -- soft warning, not fatal.
public static class ListenPortValidator
{
    // Pure-function entry point: takes the configured ListenPort and the listening addresses
    // observed post-bind (typically from IServerAddressesFeature.Addresses). Returns the
    // outcome and -- for the mismatch case -- a ready-to-log warning message that names both
    // values and the env-var lever for each side.
    public static ListenPortValidationOutcome Validate
    (
        int configuredListenPort,
        IReadOnlyCollection<string> listeningAddresses
    )
    {
        ArgumentNullException.ThrowIfNull(listeningAddresses);

        if (listeningAddresses.Count == 0)
        {
            return ListenPortValidationOutcome.Skipped("no listening addresses observed");
        }

        var observedPorts = new List<int>(listeningAddresses.Count);

        foreach (var address in listeningAddresses)
        {
            if (TryExtractPort(address, out var port))
            {
                observedPorts.Add(port);
            }
        }

        if (observedPorts.Count == 0)
        {
            return ListenPortValidationOutcome.Skipped("no parseable listen ports observed");
        }

        if (observedPorts.Contains(configuredListenPort))
        {
            return ListenPortValidationOutcome.Match(configuredListenPort);
        }

        var observedJoined = string.Join(", ", observedPorts.Select(p => p.ToString(CultureInfo.InvariantCulture)));

        // Operator-facing copy: name both observed values, name the env var that controls
        // each side, and call out the consequence (the self-route through Caddy will 502).
        // Built once with InvariantCulture so the rendered message ships unchanged into the
        // structured log; the caller passes both numeric values as structured fields.
        var warning =
            $"Hosting:ListenPort ({configuredListenPort.ToString(CultureInfo.InvariantCulture)}) does not match the API's actual listen port ({observedJoined}). "
            + $"Caddy will dial localhost:{configuredListenPort.ToString(CultureInfo.InvariantCulture)} for collabhost.collab.internal, but Kestrel is bound to {observedJoined}, so the dashboard self-route will return 502. "
            + $"Set COLLABHOST_HOSTING_LISTEN_PORT to match the listen port, or set ASPNETCORE_URLS / urls to http://localhost:{configuredListenPort.ToString(CultureInfo.InvariantCulture)} so Kestrel binds where Caddy dials.";

        return ListenPortValidationOutcome.Mismatch
        (
            configuredListenPort: configuredListenPort,
            observedPorts: observedPorts,
            renderedMessage: warning
        );
    }

    // Helper for the runtime caller: pulls the listening addresses out of IServer if the
    // feature is available. Returns an empty list when it isn't (TestServer under
    // WebApplicationFactory has no addresses, for example) so Validate's "skipped" path fires.
    public static IReadOnlyCollection<string> GetListeningAddresses(IServer server)
    {
        ArgumentNullException.ThrowIfNull(server);

        var feature = server.Features.Get<IServerAddressesFeature>();

        return feature is null ? [] : [.. feature.Addresses];
    }

    private static bool TryExtractPort(string address, out int port)
    {
        port = 0;

        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return false;
        }

        // Uri.Port returns the explicit port if one was specified, or the scheme default
        // (http=80, https=443) otherwise. For our purposes the explicit port is what the
        // operator set; the default-port case still maps cleanly because that IS the listen
        // port from the caller's perspective.
        if (uri.Port <= 0)
        {
            return false;
        }

        port = uri.Port;
        return true;
    }
}

public sealed record ListenPortValidationOutcome
{
    private ListenPortValidationOutcome
    (
        ListenPortValidationStatus status,
        int configuredListenPort,
        IReadOnlyList<int> observedPorts,
        string? renderedMessage,
        string? skipReason
    )
    {
        Status = status;
        ConfiguredListenPort = configuredListenPort;
        ObservedPorts = observedPorts;
        RenderedMessage = renderedMessage;
        SkipReason = skipReason;
    }

    public ListenPortValidationStatus Status { get; }

    public int ConfiguredListenPort { get; }

    public IReadOnlyList<int> ObservedPorts { get; }

    public string? RenderedMessage { get; }

    public string? SkipReason { get; }

    public static ListenPortValidationOutcome Match(int configuredListenPort) =>
        new
        (
            status: ListenPortValidationStatus.Match,
            configuredListenPort: configuredListenPort,
            observedPorts: [configuredListenPort],
            renderedMessage: null,
            skipReason: null
        );

    public static ListenPortValidationOutcome Mismatch
    (
        int configuredListenPort,
        IReadOnlyList<int> observedPorts,
        string renderedMessage
    ) =>
        new
        (
            status: ListenPortValidationStatus.Mismatch,
            configuredListenPort: configuredListenPort,
            observedPorts: observedPorts,
            renderedMessage: renderedMessage,
            skipReason: null
        );

    public static ListenPortValidationOutcome Skipped(string reason) =>
        new
        (
            status: ListenPortValidationStatus.Skipped,
            configuredListenPort: 0,
            observedPorts: [],
            renderedMessage: null,
            skipReason: reason
        );
}

public enum ListenPortValidationStatus
{
    Match,
    Mismatch,
    Skipped
}
