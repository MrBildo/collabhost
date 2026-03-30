using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Domain.Entities;

namespace Collabhost.Api.Features.Apps;

public static class Restart
{
    public static async Task<Results<Ok<ProcessStatusResponse>, NotFound, ProblemHttpResult>> HandleAsync
    (
        string externalId,
        CommandDispatcher dispatcher,
        CancellationToken ct
    )
    {
        var result = await dispatcher.DispatchAsync(new RestartCommand(externalId), ct);

        Results<Ok<ProcessStatusResponse>, NotFound, ProblemHttpResult> response = result switch
        {
            { IsSuccess: true } => TypedResults.Ok(result.Value),
            { ErrorCode: "NOT_FOUND" } => TypedResults.NotFound(),
            _ => TypedResults.Problem(result.ErrorMessage, statusCode: 400)
        };

        return response;
    }
}

public record RestartCommand(string ExternalId) : ICommand<ProcessStatusResponse>;

public class RestartCommandHandler
(
    CollabhostDbContext db,
    ProcessSupervisor supervisor
) : ICommandHandler<RestartCommand, ProcessStatusResponse>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly ProcessSupervisor _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));

    public async Task<CommandResult<ProcessStatusResponse>> HandleAsync(RestartCommand command, CancellationToken ct = default)
    {
        var app = await _db.FindAppByExternalIdAsync(command.ExternalId, ct);

        if (app is null)
        {
            return CommandResult<ProcessStatusResponse>.Fail("NOT_FOUND", "App not found.");
        }

        // Check if app type has process capability
        var hasProcess = await _db.Set<AppTypeCapability>()
            .AnyAsync(atc => atc.AppTypeId == app.AppTypeId && atc.CapabilityId == IdentifierCatalog.Capabilities.Process, ct);

        if (!hasProcess)
        {
            return CommandResult<ProcessStatusResponse>.Fail("NO_PROCESS", "This app type has no process to manage.");
        }

        var managed = await _supervisor.RestartAppAsync(app.Id, ct);
        return CommandResult<ProcessStatusResponse>.Success(ProcessStatusMapper.Map(managed));
    }
}
