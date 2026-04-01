using Collabhost.Api.Domain.Lookups;

namespace Collabhost.Api.Features.Lookups;

public static class GetServeModes
{
    public record Response
    (
        string Name,
        string DisplayName
    );

    public static async Task<Ok<List<Response>>> HandleAsync
    (
        CommandDispatcher dispatcher,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(new GetServeModesCommand(), cancellationToken);

        return TypedResults.Ok(result.Value!);
    }
}

public record GetServeModesCommand : ICommand<List<GetServeModes.Response>>;

public sealed class GetServeModesCommandHandler(CollabhostDbContext db)
    : ICommandHandler<GetServeModesCommand, List<GetServeModes.Response>>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<CommandResult<List<GetServeModes.Response>>> HandleAsync
    (
        GetServeModesCommand command,
        CancellationToken ct = default
    )
    {
        var results = await _db.Set<ServeMode>()
            .Where(e => e.IsActive)
            .OrderBy(e => e.Ordinal)
            .Select
            (
                e => new GetServeModes.Response
                (
                    e.Name,
                    e.DisplayName
                )
            )
            .ToListAsync(ct);

        return CommandResult<List<GetServeModes.Response>>.Success(results);
    }
}
