namespace Collabhost.Api.Features.Apps;

public static class GetStatus
{
    public static async Task<Results<Ok<ProcessStatusResponse>, NotFound>> HandleAsync
    (
        string externalId,
        CommandDispatcher dispatcher,
        CancellationToken ct
    )
    {
        var result = await dispatcher.DispatchAsync(new GetStatusCommand(externalId), ct);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.NotFound();
    }
}

public record GetStatusCommand(string ExternalId) : ICommand<ProcessStatusResponse>;

public class GetStatusCommandHandler
(
    CollabhostDbContext db,
    ProcessSupervisor supervisor
) : ICommandHandler<GetStatusCommand, ProcessStatusResponse>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly ProcessSupervisor _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));

    public async Task<CommandResult<ProcessStatusResponse>> HandleAsync(GetStatusCommand command, CancellationToken ct = default)
    {
        var app = await _db.FindAppByExternalIdAsync(command.ExternalId, ct);

        if (app is null)
        {
            return CommandResult<ProcessStatusResponse>.Fail("NOT_FOUND", "App not found.");
        }

        var managed = _supervisor.GetStatus(app.Id);

        var response = managed is not null
            ? ProcessStatusMapper.Map(managed)
            : ProcessStatusMapper.Stopped(app.ExternalId, app.DisplayName);

        return CommandResult<ProcessStatusResponse>.Success(response);
    }
}
