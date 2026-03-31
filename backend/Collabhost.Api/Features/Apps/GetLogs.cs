namespace Collabhost.Api.Features.Apps;

public static class GetLogs
{
    public record LogEntryResponse(DateTime Timestamp, string Stream, string Content);

    public record Response(IReadOnlyList<LogEntryResponse> Entries, int TotalBuffered);

    public static async Task<Results<Ok<Response>, NotFound, ProblemHttpResult>> HandleAsync
    (
        string externalId,
        CommandDispatcher dispatcher,
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

        var result = await dispatcher.DispatchAsync(new GetLogsCommand(externalId, count, streamFilter), ct);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.NotFound();
    }
}

public record GetLogsCommand(string ExternalId, int Count, LogStream? StreamFilter) : ICommand<GetLogs.Response>;

public class GetLogsCommandHandler
(
    CollabhostDbContext db,
    ProcessSupervisor supervisor
) : ICommandHandler<GetLogsCommand, GetLogs.Response>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly ProcessSupervisor _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));

    public async Task<CommandResult<GetLogs.Response>> HandleAsync(GetLogsCommand command, CancellationToken ct = default)
    {
        var app = await _db.FindAppByExternalIdAsync(command.ExternalId, ct);

        if (app is null)
        {
            return CommandResult<GetLogs.Response>.Fail("NOT_FOUND", "App not found.");
        }

        var managed = _supervisor.GetProcess(app.Id);

        if (managed is null)
        {
            return CommandResult<GetLogs.Response>.Success(new GetLogs.Response([], 0));
        }

        var totalBuffered = managed.LogBuffer.Count;
        var count = Math.Clamp(command.Count, 1, 1000);

        var entries = command.StreamFilter is not null
            ? [.. managed.LogBuffer
                .GetAll()
                .Where(e => e.Stream == command.StreamFilter.Value)
                .TakeLast(count)
                .Select
                (
                    e => new GetLogs.LogEntryResponse(e.Timestamp, e.Stream.ToString(), e.Content)
                )]
            : (IReadOnlyList<GetLogs.LogEntryResponse>)[.. managed.LogBuffer
                .GetLast(count)
                .Select
                (
                    e => new GetLogs.LogEntryResponse(e.Timestamp, e.Stream.ToString(), e.Content)
                )];

        return CommandResult<GetLogs.Response>.Success(new GetLogs.Response(entries, totalBuffered));
    }
}
