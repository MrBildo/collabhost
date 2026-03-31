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

public sealed class GetStatusCommandHandler
(
    CollabhostDbContext db,
    ProcessSupervisor supervisor,
    IProcessStateNameResolver stateNameResolver
) : ICommandHandler<GetStatusCommand, ProcessStatusResponse>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly ProcessSupervisor _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
    private readonly IProcessStateNameResolver _stateNameResolver = stateNameResolver ?? throw new ArgumentNullException(nameof(stateNameResolver));

    public async Task<CommandResult<ProcessStatusResponse>> HandleAsync(GetStatusCommand command, CancellationToken ct = default)
    {
        var app = await _db.FindAppByExternalIdAsync(command.ExternalId, ct);

        if (app is null)
        {
            return CommandResult<ProcessStatusResponse>.Fail("NOT_FOUND", "App not found.");
        }

        var managed = _supervisor.GetProcess(app.Id);

        var response = managed is not null
            ? await ProcessStatusMapper.MapAsync(managed, _stateNameResolver, ct)
            : await ProcessStatusMapper.StoppedAsync(app.ExternalId, app.DisplayName, _stateNameResolver, ct);

        return CommandResult<ProcessStatusResponse>.Success(response);
    }
}
