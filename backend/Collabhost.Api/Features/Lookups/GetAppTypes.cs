using Collabhost.Api.Domain.Lookups;

namespace Collabhost.Api.Features.Lookups;

public static class GetAppTypes
{
    public record Response
    (
        string Id,
        string Name,
        string DisplayName
    );

    public static async Task<Ok<List<Response>>> HandleAsync
    (
        CommandDispatcher dispatcher,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(new GetAppTypesCommand(), cancellationToken);

        return TypedResults.Ok(result.Value!);
    }
}

public record GetAppTypesCommand : ICommand<List<GetAppTypes.Response>>;

public sealed class GetAppTypesCommandHandler(CollabhostDbContext db)
    : ICommandHandler<GetAppTypesCommand, List<GetAppTypes.Response>>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<CommandResult<List<GetAppTypes.Response>>> HandleAsync
    (
        GetAppTypesCommand command,
        CancellationToken ct = default
    )
    {
        var results = await _db.Set<AppType>()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Ordinal)
            .Select(t => new GetAppTypes.Response
            (
                t.Id.ToString(),
                t.Name,
                t.DisplayName
            ))
            .ToListAsync(ct);

        return CommandResult<List<GetAppTypes.Response>>.Success(results);
    }
}
