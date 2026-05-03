using System.Globalization;

using Collabhost.Api.Portal;
using Collabhost.Api.Proxy;

using Microsoft.AspNetCore.Http.HttpResults;

namespace Collabhost.Api.Platform;

public static class SystemEndpoints
{
    private static readonly DateTime _startedAt = DateTime.UtcNow;

    public static void Map(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1").WithTags("System");

        group.MapGet("/status", GetStatus);
        group.MapGet("/version", GetVersion);
    }

    private static Ok<SystemStatus> GetStatus
    (
        ProxyManager proxyManager,
        PortalSettings portalSettings,
        ProxySettings proxySettings
    )
    {
        var uptimeSeconds = Math.Max(0, (DateTime.UtcNow - _startedAt).TotalSeconds);

        // Enum name is lowercased at the boundary; internal code stays type-safe.
        var currentState = proxyManager.CurrentState;

        var proxyState = currentState
            .ToString()
            .ToLowerInvariant();

        // Portal URL composition. https:// is correct -- Caddy terminates TLS on the
        // self-route via `tls internal`. Operator TLS-trust (browser warning on the
        // self-signed cert) is a separate concern handled by the future ACME-issuer card.
        var portalUrl = $"https://{portalSettings.Subdomain}.{proxySettings.BaseDomain}";

        // portalReachable is a pure function of proxyState: only Running means routes are
        // reaching the public listener. degraded / failed / disabled / stopped / starting
        // all mean the Portal is not currently reachable. Card #217.
        var portalReachable = currentState == ProxyState.Running;

        var proxyDetail = BuildProxyDetail(currentState, proxyManager.LastSyncOutcome, proxySettings);

        var status = new SystemStatus
        (
            "ok",
            VersionInfo.Current,
            Environment.MachineName,
            Math.Round(uptimeSeconds, 1),
            DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            proxyState,
            portalUrl,
            portalReachable,
            proxyDetail
        );

        return TypedResults.Ok(status);
    }

    // proxyDetail is surfaced when the operator is most likely to need it: the proxy is
    // Degraded (sync failed, this is the why) or Failed (admin probe failed, sync may
    // never have run). For Running / Starting / Stopped / Disabled it stays null so the
    // healthy path's response shape is unchanged. Card #217.
    private static ProxyDetail? BuildProxyDetail
    (
        ProxyState state,
        SyncOutcome lastSync,
        ProxySettings proxySettings
    )
    {
        if (state is not (ProxyState.Degraded or ProxyState.Failed))
        {
            return null;
        }

        var lastSyncAt = lastSync.LastSyncAt?.ToString("o", CultureInfo.InvariantCulture);

        return new ProxyDetail
        (
            LastSyncOk: lastSync.Success,
            LastSyncError: lastSync.ErrorMessage,
            LastSyncAt: lastSyncAt,
            ListenAddress: proxySettings.ListenAddress
        );
    }

    private static Ok<VersionResponse> GetVersion() =>
        TypedResults.Ok
        (
            new VersionResponse
            (
                VersionInfo.Current,
                VersionInfo.Commit,
                VersionInfo.Platform
            )
        );
}
