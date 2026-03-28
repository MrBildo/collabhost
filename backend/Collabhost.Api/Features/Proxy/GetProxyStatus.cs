using Collabhost.Api.Common;
using Collabhost.Api.Data;
using Collabhost.Api.Domain;
using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Services;

namespace Collabhost.Api.Features.Proxy;

public static class GetProxyStatus
{
    public record ProxyAppRow(Guid Id, string ExternalId);

    public record RoutableAppRow(Guid AppTypeId);

    public static async Task<Results<Ok<ProxyStatusResponse>, ProblemHttpResult>> HandleAsync
    (
        GetProxyStatusQueryHandler handler,
        CancellationToken ct
    )
    {
        var result = await handler.HandleAsync(ct);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.Problem(result.ErrorMessage, statusCode: 500);
    }
}

public class GetProxyStatusQueryHandler
(
    CollabhostDbContext db,
    ProcessSupervisor supervisor,
    IProxyConfigClient proxyClient,
    ProxySettings settings
)
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly ProcessSupervisor _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
    private readonly IProxyConfigClient _proxyClient = proxyClient ?? throw new ArgumentNullException(nameof(proxyClient));
    private readonly ProxySettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));

#pragma warning disable MA0051 // Long method justified — multi-step proxy status aggregation
    public async Task<QueryResult<ProxyStatusResponse>> HandleAsync(CancellationToken ct = default)
    {
        // Find the proxy service app
        var proxyServiceTypeId = IdentifierCatalog.AppTypes.ProxyService;
        var proxyApp = await _db.Database
            .SqlQuery<GetProxyStatus.ProxyAppRow>(
                $"""
                SELECT
                    A.[Id]
                    ,A.[ExternalId]
                FROM
                    [App] A
                WHERE
                    A.[AppTypeId] = {proxyServiceTypeId}
                """)
            .SingleOrDefaultAsync(ct);

        // Determine process state
        string state;
        int? pid = null;

        if (proxyApp is not null)
        {
            var managed = _supervisor.GetStatus(proxyApp.Id);
            if (managed is not null)
            {
                state = managed.IsRunning ? "Running"
                    : managed.IsCrashed ? "Crashed"
                    : managed.IsRestarting ? "Restarting"
                    : "Stopped";
                pid = managed.Pid;
            }
            else
            {
                state = "Stopped";
            }
        }
        else
        {
            state = "NotRegistered";
        }

        // Check admin API readiness
        var adminApiReady = await _proxyClient.IsReadyAsync(ct);

        // Count routable apps
        var allApps = await _db.Database
            .SqlQuery<GetProxyStatus.RoutableAppRow>(
                $"""
                SELECT
                    A.[AppTypeId]
                FROM
                    [App] A
                """)
            .ToListAsync(ct);

        var routeCount = allApps.Count(a => AppTypeBehavior.IsRoutable(a.AppTypeId));

        return QueryResult<ProxyStatusResponse>.Success
        (
            new ProxyStatusResponse
            (
                state,
                pid,
                adminApiReady,
                routeCount,
                _settings.BaseDomain
            )
        );
    }
#pragma warning restore MA0051
}
