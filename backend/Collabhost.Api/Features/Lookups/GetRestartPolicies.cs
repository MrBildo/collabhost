using Collabhost.Api.Domain.Lookups;

namespace Collabhost.Api.Features.Lookups;

public static class GetRestartPolicies
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
        var result = await dispatcher.DispatchAsync(new GetRestartPoliciesCommand(), cancellationToken);

        return TypedResults.Ok(result.Value!);
    }
}

public record GetRestartPoliciesCommand : ICommand<List<GetRestartPolicies.Response>>;

public sealed class GetRestartPoliciesCommandHandler(CollabhostDbContext db)
    : ICommandHandler<GetRestartPoliciesCommand, List<GetRestartPolicies.Response>>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<CommandResult<List<GetRestartPolicies.Response>>> HandleAsync
    (
        GetRestartPoliciesCommand command,
        CancellationToken ct = default
    )
    {
        var results = await _db.Set<RestartPolicy>()
            .Where(e => e.IsActive)
            .OrderBy(e => e.Ordinal)
            .Select
            (
                e => new GetRestartPolicies.Response
                (
                    e.Name,
                    e.DisplayName
                )
            )
            .ToListAsync(ct);

        return CommandResult<List<GetRestartPolicies.Response>>.Success(results);
    }
}
