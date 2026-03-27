using Collabhost.Api.Common;
using Collabhost.Api.Data;
using Collabhost.Api.Domain;
using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Services;

namespace Collabhost.Api.Features.Proxy;

public static class GetProxyStatus
{
    public record ProxyAppRow(Guid Id, string ExternalId);

    public record RoutableAppCount(int Count);

    public class Handler
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

        public async Task<QueryResult<ProxyStatusResponse>> HandleAsync(CancellationToken ct = default)
        {
            // Find the proxy service app
            var proxyServiceTypeId = IdentifierCatalog.AppTypes.ProxyService;
            var proxyApp = await _db.Database
                .SqlQuery<ProxyAppRow>(
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
                .SqlQuery<RoutableAppRow>(
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
    }

    public record RoutableAppRow(Guid AppTypeId);

    public static async Task<Results<Ok<ProxyStatusResponse>, ProblemHttpResult>> HandleAsync
    (
        Handler handler,
        CancellationToken ct
    )
    {
        var result = await handler.HandleAsync(ct);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.Problem(result.ErrorMessage, statusCode: 500);
    }
}
