using Collabhost.Api.Domain.Entities;

namespace Collabhost.Api.Features.Capabilities;

public static class GetAll
{
    public record Response
    (
        string Slug,
        string DisplayName,
        string? Description,
        string Category
    );

    public static async Task<Ok<List<Response>>> HandleAsync
    (
        CommandDispatcher dispatcher,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(new GetAllCapabilitiesCommand(), cancellationToken);

        return TypedResults.Ok(result.Value!);
    }
}

public record GetAllCapabilitiesCommand : ICommand<List<GetAll.Response>>;

public sealed class GetAllCapabilitiesCommandHandler(CollabhostDbContext db)
    : ICommandHandler<GetAllCapabilitiesCommand, List<GetAll.Response>>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<CommandResult<List<GetAll.Response>>> HandleAsync
    (
        GetAllCapabilitiesCommand command,
        CancellationToken ct = default
    )
    {
        var results = await _db.Set<Capability>()
            .OrderBy(c => c.Category)
            .ThenBy(c => c.Slug)
            .Select(c => new GetAll.Response
            (
                c.Slug,
                c.DisplayName,
                c.Description,
                c.Category
            ))
            .ToListAsync(ct);

        return CommandResult<List<GetAll.Response>>.Success(results);
    }
}
