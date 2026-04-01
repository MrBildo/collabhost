using Collabhost.Api.Domain.Lookups;

namespace Collabhost.Api.Features.Lookups;

public static class GetDiscoveryStrategies
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
        var result = await dispatcher.DispatchAsync(new GetDiscoveryStrategiesCommand(), cancellationToken);

        return TypedResults.Ok(result.Value!);
    }
}

public record GetDiscoveryStrategiesCommand : ICommand<List<GetDiscoveryStrategies.Response>>;

public sealed class GetDiscoveryStrategiesCommandHandler(CollabhostDbContext db)
    : ICommandHandler<GetDiscoveryStrategiesCommand, List<GetDiscoveryStrategies.Response>>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<CommandResult<List<GetDiscoveryStrategies.Response>>> HandleAsync
    (
        GetDiscoveryStrategiesCommand command,
        CancellationToken ct = default
    )
    {
        var results = await _db.Set<DiscoveryStrategy>()
            .Where(e => e.IsActive)
            .OrderBy(e => e.Ordinal)
            .Select
            (
                e => new GetDiscoveryStrategies.Response
                (
                    e.Name,
                    e.DisplayName
                )
            )
            .ToListAsync(ct);

        return CommandResult<List<GetDiscoveryStrategies.Response>>.Success(results);
    }
}
