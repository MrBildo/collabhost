using Collabhost.Api.Common;
using Collabhost.Api.Data;
using Collabhost.Api.Domain;
using Collabhost.Api.Services;

namespace Collabhost.Api.Features.Apps;

public static class Delete
{
    public record Command(string ExternalId);

    public class Handler
    (
        CollabhostDbContext db,
        ProxyConfigManager proxyConfigManager
    )
    {
        private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));
        private readonly ProxyConfigManager _proxyConfigManager = proxyConfigManager ?? throw new ArgumentNullException(nameof(proxyConfigManager));

        public async Task<CommandResult> HandleAsync(Command command, CancellationToken ct = default)
        {
            var app = await _db.Apps
                .Include(a => a.EnvironmentVariables)
                .SingleOrDefaultAsync(a => a.ExternalId == command.ExternalId, ct);

            if (app is null)
            {
                return CommandResult.Fail("NOT_FOUND", "App not found.");
            }

            if (!AppTypeBehavior.IsDeletable(app.AppTypeId))
            {
                return CommandResult.Fail("PROTECTED", "This app type cannot be deleted.");
            }

            _db.Apps.Remove(app);
            await _db.SaveChangesAsync(ct);

            _ = _proxyConfigManager.SyncRoutesAsync();

            return CommandResult.Success();
        }
    }

    public static async Task<Results<NoContent, NotFound, ProblemHttpResult>> HandleAsync
    (
        string externalId,
        Handler handler,
        CancellationToken ct
    )
    {
        var result = await handler.HandleAsync(new Command(externalId), ct);

        Results<NoContent, NotFound, ProblemHttpResult> response = result switch
        {
            { IsSuccess: true } => TypedResults.NoContent(),
            { ErrorCode: "NOT_FOUND" } => TypedResults.NotFound(),
            { ErrorCode: "PROTECTED" } => TypedResults.Problem(result.ErrorMessage, statusCode: 403),
            _ => TypedResults.Problem(result.ErrorMessage, statusCode: 400)
        };

        return response;
    }
}
