using Collabhost.Api.Common;
using Collabhost.Api.Data;
using Collabhost.Api.Services;

namespace Collabhost.Api.Features.Apps;

public static class GetStatus
{
    public record Query(string ExternalId);

    public static async Task<Results<Ok<ProcessStatusResponse>, NotFound>> HandleAsync
    (
        string externalId,
        GetStatusQueryHandler handler,
        CancellationToken ct
    )
    {
        var result = await handler.HandleAsync(new Query(externalId), ct);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.NotFound();
    }
}

public class GetStatusQueryHandler
(
    CollabhostDbContext db,
    ProcessSupervisor supervisor
)
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly ProcessSupervisor _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));

    public async Task<QueryResult<ProcessStatusResponse>> HandleAsync(GetStatus.Query query, CancellationToken ct = default)
    {
        var app = await _db.FindAppByExternalIdAsync(query.ExternalId, ct);

        if (app is null)
        {
            return QueryResult<ProcessStatusResponse>.Fail("App not found.");
        }

        var managed = _supervisor.GetStatus(app.Id);

        var response = managed is not null
            ? ProcessStatusMapper.Map(managed)
            : ProcessStatusMapper.Stopped(app.ExternalId, app.DisplayName);

        return QueryResult<ProcessStatusResponse>.Success(response);
    }
}
