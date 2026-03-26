using Collabhost.Api.Common;
using Collabhost.Api.Data;

namespace Collabhost.Api.Features.Apps;

public static class Delete
{
    public record Command(string ExternalId);

    public class Handler(CollabhostDbContext db)
    {
        private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));

        public async Task<CommandResult> HandleAsync(Command command, CancellationToken ct = default)
        {
            var app = await _db.Apps
                .Include(a => a.EnvironmentVariables)
                .FirstOrDefaultAsync(a => a.ExternalId == command.ExternalId, ct);

            if (app is null)
            {
                return CommandResult.Fail("NOT_FOUND", "App not found.");
            }

            _db.Apps.Remove(app);
            await _db.SaveChangesAsync(ct);

            return CommandResult.Success();
        }
    }

    public static async Task<Results<NoContent, NotFound>> HandleAsync
    (
        string externalId,
        Handler handler,
        CancellationToken ct
    )
    {
        var result = await handler.HandleAsync(new Command(externalId), ct);

        return result.IsSuccess
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }
}
