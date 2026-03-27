using Collabhost.Api.Common;
using Collabhost.Api.Data;
using Collabhost.Api.Domain;
using Collabhost.Api.Services;

namespace Collabhost.Api.Features.Apps;

public static class Start
{
    public record Command(string ExternalId);

    public class Handler
    (
        CollabhostDbContext db,
        ProcessSupervisor supervisor
    )
    {
        private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));
        private readonly ProcessSupervisor _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));

        public async Task<CommandResult<ProcessStatusResponse>> HandleAsync(Command command, CancellationToken ct = default)
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
                var managed = await _supervisor.StartAppAsync(app.Id, ct);
                return CommandResult<ProcessStatusResponse>.Success(ProcessStatusMapper.Map(managed));
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already running"))
            {
                return CommandResult<ProcessStatusResponse>.Fail("ALREADY_RUNNING", ex.Message);
            }
        }
    }

    public static async Task<Results<Ok<ProcessStatusResponse>, NotFound, ProblemHttpResult>> HandleAsync
    (
        string externalId,
        Handler handler,
        CancellationToken ct
    )
    {
        var result = await handler.HandleAsync(new Command(externalId), ct);

        Results<Ok<ProcessStatusResponse>, NotFound, ProblemHttpResult> response = result switch
        {
            { IsSuccess: true } => TypedResults.Ok(result.Value),
            { ErrorCode: "NOT_FOUND" } => TypedResults.NotFound(),
            { ErrorCode: "ALREADY_RUNNING" } => TypedResults.Problem(result.ErrorMessage, statusCode: 409),
            _ => TypedResults.Problem(result.ErrorMessage, statusCode: 400)
        };

        return response;
    }
}
