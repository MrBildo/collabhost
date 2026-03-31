using Collabhost.Api.Domain.Entities;

namespace Collabhost.Api.Features.AppTypes;

public static class Delete
{
    public static async Task<Results<NoContent, NotFound, ProblemHttpResult>> HandleAsync
    (
        string externalId,
        CommandDispatcher dispatcher,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(new DeleteAppTypeCommand(externalId), cancellationToken);

        Results<NoContent, NotFound, ProblemHttpResult> response = result switch
        {
            { IsSuccess: true } => TypedResults.NoContent(),
            { ErrorCode: "NOT_FOUND" } => TypedResults.NotFound(),
            { ErrorCode: "CONFLICT" } => TypedResults.Problem(result.ErrorMessage, statusCode: 409),
            _ => TypedResults.Problem(result.ErrorMessage, statusCode: 400)
        };

        return response;
    }
}

public record DeleteAppTypeCommand(string ExternalId) : ICommand<Empty>;

public sealed class DeleteAppTypeCommandHandler(CollabhostDbContext db)
    : ICommandHandler<DeleteAppTypeCommand, Empty>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<CommandResult<Empty>> HandleAsync
    (
        DeleteAppTypeCommand command,
        CancellationToken ct = default
    )
    {
        var appType = await _db.Set<AppType>()
            .SingleOrDefaultAsync(t => t.ExternalId == command.ExternalId, ct);

        if (appType is null)
        {
            return CommandResult<Empty>.Fail("NOT_FOUND", "App type not found.");
        }

        if (appType.IsBuiltIn)
        {
            return CommandResult<Empty>.Fail("CONFLICT", "Built-in app types cannot be deleted.");
        }

        var referencingApps = await _db.Apps
            .Where(a => a.AppTypeId == appType.Id)
            .Select(a => a.Name)
            .ToListAsync(ct);

        if (referencingApps.Count > 0)
        {
            var appNames = string.Join(", ", referencingApps.Select(n => $"'{n}'"));
            return CommandResult<Empty>.Fail("CONFLICT", $"Cannot delete app type — referenced by apps: {appNames}.");
        }

        _db.Set<AppType>().Remove(appType);
        await _db.SaveChangesAsync(ct);

        return CommandResult<Empty>.Success(Empty.Value);
    }
}
