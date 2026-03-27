using Collabhost.Api.Common;
using Collabhost.Api.Data;
using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Services;

namespace Collabhost.Api.Features.Apps;

public static class Restart
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

            if (app.AppTypeId == IdentifierCatalog.AppTypes.StaticSite)
            {
                return CommandResult<ProcessStatusResponse>.Fail("STATIC_SITE", "Static sites have no process to manage.");
            }

            var managed = await _supervisor.RestartAppAsync(app.Id, ct);
            return CommandResult<ProcessStatusResponse>.Success(ProcessStatusMapper.Map(managed));
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
            _ => TypedResults.Problem(result.ErrorMessage, statusCode: 400)
        };

        return response;
    }
}
