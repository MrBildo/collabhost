using Collabhost.Api.Common;
using Collabhost.Api.Data;
using Collabhost.Api.Services;

namespace Collabhost.Api.Features.Apps;

public static class GetLogs
{
    public record Query(string ExternalId, int Count, LogStream? StreamFilter);

    public record LogEntryResponse(DateTime Timestamp, string Stream, string Content);

    public record Response(IReadOnlyList<LogEntryResponse> Entries, int TotalBuffered);

    public static async Task<Results<Ok<Response>, NotFound, ProblemHttpResult>> HandleAsync
    (
        string externalId,
        GetLogsQueryHandler handler,
        CancellationToken ct,
        int count = 100,
        string? stream = null
    )
    {
        if (stream is not null)
        {
            var normalized = stream.ToLowerInvariant();
            if (normalized is not ("stdout" or "stderr"))
            {
                return TypedResults.Problem("Invalid stream value. Must be 'stdout' or 'stderr'.", statusCode: 400);
            }
        }

        var streamFilter = stream?.ToLowerInvariant() switch
        {
            "stdout" => (LogStream?)LogStream.StdOut,
            "stderr" => LogStream.StdErr,
            _ => null
        };

        var result = await handler.HandleAsync(new Query(externalId, count, streamFilter), ct);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.NotFound();
    }
}

public class GetLogsQueryHandler
(
    CollabhostDbContext db,
    ProcessSupervisor supervisor
)
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly ProcessSupervisor _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));

    public async Task<QueryResult<GetLogs.Response>> HandleAsync(GetLogs.Query query, CancellationToken ct = default)
    {
        var app = await _db.FindAppByExternalIdAsync(query.ExternalId, ct);

        if (app is null)
        {
            return QueryResult<GetLogs.Response>.Fail("App not found.");
        }

        var managed = _supervisor.GetStatus(app.Id);

        if (managed is null)
        {
            return QueryResult<GetLogs.Response>.Success(new GetLogs.Response([], 0));
        }

        var totalBuffered = managed.LogBuffer.Count;
        var count = Math.Clamp(query.Count, 1, 1000);

        var entries = query.StreamFilter is not null
            ? [.. managed.LogBuffer
                .GetAll()
                .Where(e => e.Stream == query.StreamFilter.Value)
                .TakeLast(count)
                .Select(e => new GetLogs.LogEntryResponse(e.Timestamp, e.Stream.ToString(), e.Content))]
            : (IReadOnlyList<GetLogs.LogEntryResponse>)[.. managed.LogBuffer
                .GetLast(count)
                .Select(e => new GetLogs.LogEntryResponse(e.Timestamp, e.Stream.ToString(), e.Content))];

        return QueryResult<GetLogs.Response>.Success(new GetLogs.Response(entries, totalBuffered));
    }
}
