using Collabhost.Api.Common;
using Collabhost.Api.Data;
using Collabhost.Api.Services;

namespace Collabhost.Api.Features.Apps;

public static class GetStatus
{
    public record Query(string ExternalId);

    public class Handler
    (
        CollabhostDbContext db,
        ProcessSupervisor supervisor
    )
    {
        private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));
        private readonly ProcessSupervisor _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));

        public async Task<QueryResult<ProcessStatusResponse>> HandleAsync(Query query, CancellationToken ct = default)
        {
            var app = await _db.Database
                .SqlQuery<AppLookup>(
                    $"""
                    SELECT
                        A.[Id]
                        ,A.[ExternalId]
                        ,A.[Name]
                    FROM
                        [App] A
                    WHERE
                        A.[ExternalId] = {query.ExternalId}
                    """)
                .SingleOrDefaultAsync(ct);

            if (app is null)
            {
                return QueryResult<ProcessStatusResponse>.Fail("App not found.");
            }

            var managed = _supervisor.GetStatus(app.Id);

            var response = managed is not null
                ? ProcessStatusMapper.Map(managed)
                : ProcessStatusMapper.Stopped(app.ExternalId, app.Name);

            return QueryResult<ProcessStatusResponse>.Success(response);
        }
    }

    public static async Task<Results<Ok<ProcessStatusResponse>, NotFound>> HandleAsync
    (
        string externalId,
        Handler handler,
        CancellationToken ct
    )
    {
        var result = await handler.HandleAsync(new Query(externalId), ct);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.NotFound();
    }

    internal record AppLookup(Guid Id, string ExternalId, string Name);
}
