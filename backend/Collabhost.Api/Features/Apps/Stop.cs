using Collabhost.Api.Common;
using Collabhost.Api.Data;
using Collabhost.Api.Domain;
using Collabhost.Api.Services;

namespace Collabhost.Api.Features.Apps;

public static class Stop
{
    public static async Task<Results<Ok<ProcessStatusResponse>, NotFound, ProblemHttpResult>> HandleAsync
    (
        string externalId,
        CommandDispatcher dispatcher,
        CancellationToken ct
    )
    {
        var result = await dispatcher.DispatchAsync<StopCommand, ProcessStatusResponse>(new StopCommand(externalId), ct);

        Results<Ok<ProcessStatusResponse>, NotFound, ProblemHttpResult> response = result switch
        {
            { IsSuccess: true } => TypedResults.Ok(result.Value),
            { ErrorCode: "NOT_FOUND" } => TypedResults.NotFound(),
            { ErrorCode: "ALREADY_STOPPED" } => TypedResults.Problem(result.ErrorMessage, statusCode: 409),
            _ => TypedResults.Problem(result.ErrorMessage, statusCode: 400)
        };

        return response;
    }
}

public record StopCommand(string ExternalId) : ICommand<ProcessStatusResponse>;

public class StopCommandHandler
(
    CollabhostDbContext db,
    ProcessSupervisor supervisor
) : ICommandHandler<StopCommand, ProcessStatusResponse>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly ProcessSupervisor _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));

    public async Task<CommandResult<ProcessStatusResponse>> HandleAsync(StopCommand command, CancellationToken ct = default)
    {
        var app = await _db.FindAppByExternalIdAsync(command.ExternalId, ct);

        if (app is null)
        {
            return CommandResult<ProcessStatusResponse>.Fail("NOT_FOUND", "App not found.");
        }

        if (!AppTypeBehavior.HasProcess(app.AppTypeId))
        {
            return CommandResult<ProcessStatusResponse>.Fail("NO_PROCESS", "This app type has no process to manage.");
        }

        try
        {
            var managed = await _supervisor.StopAppAsync(app.Id, ct);
            return CommandResult<ProcessStatusResponse>.Success(ProcessStatusMapper.Map(managed));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already stopped"))
        {
            return CommandResult<ProcessStatusResponse>.Fail("ALREADY_STOPPED", ex.Message);
        }
    }
}
