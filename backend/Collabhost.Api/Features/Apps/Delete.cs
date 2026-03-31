using Collabhost.Api.Domain.Entities;

namespace Collabhost.Api.Features.Apps;

public static class Delete
{
    public static async Task<Results<NoContent, NotFound, ProblemHttpResult>> HandleAsync
    (
        string externalId,
        CommandDispatcher dispatcher,
        CancellationToken ct
    )
    {
        var result = await dispatcher.DispatchAsync(new DeleteCommand(externalId), ct);

        Results<NoContent, NotFound, ProblemHttpResult> response = result switch
        {
            { IsSuccess: true } => TypedResults.NoContent(),
            { ErrorCode: "NOT_FOUND" } => TypedResults.NotFound(),
            _ => TypedResults.Problem(result.ErrorMessage, statusCode: 400)
        };

        return response;
    }
}

public record DeleteCommand(string ExternalId) : ICommand<Empty>;

public class DeleteCommandHandler
(
    CollabhostDbContext db,
    ProxyConfigManager proxyConfigManager
) : ICommandHandler<DeleteCommand, Empty>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly ProxyConfigManager _proxyConfigManager = proxyConfigManager ?? throw new ArgumentNullException(nameof(proxyConfigManager));

    public async Task<CommandResult<Empty>> HandleAsync(DeleteCommand command, CancellationToken ct = default)
    {
        var app = await _db.Apps
            .SingleOrDefaultAsync(a => a.ExternalId == command.ExternalId, ct);

        if (app is null)
        {
            return CommandResult<Empty>.Fail("NOT_FOUND", "App not found.");
        }

        _db.Apps.Remove(app);
        await _db.SaveChangesAsync(ct);

        await _proxyConfigManager.SyncRoutesAsync(ct);

        return CommandResult<Empty>.Success(Empty.Value);
    }
}
