namespace Collabhost.Api.Features.Proxy;

public static class GetRoutes
{
    public record RouteQueryRow
    (
        string ExternalId,
        string Name,
        string DisplayName,
        Guid AppTypeId,
        int? Port,
        string? InstallDirectory
    );

    public static async Task<Results<Ok<RouteListResponse>, ProblemHttpResult>> HandleAsync
    (
        CommandDispatcher dispatcher,
        CancellationToken ct
    )
    {
        var result = await dispatcher.DispatchAsync(new GetRoutesCommand(), ct);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.Problem(result.ErrorMessage, statusCode: 500);
    }
}

public record GetRoutesCommand : ICommand<RouteListResponse>;

public class GetRoutesCommandHandler
(
    CollabhostDbContext db,
    ProxySettings settings
) : ICommandHandler<GetRoutesCommand, RouteListResponse>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly ProxySettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    public async Task<CommandResult<RouteListResponse>> HandleAsync(GetRoutesCommand command, CancellationToken ct = default)
    {
        var rows = await _db.Database
            .SqlQuery<GetRoutes.RouteQueryRow>(
                $"""
                SELECT
                    A.[ExternalId]
                    ,A.[Name]
                    ,A.[DisplayName]
                    ,A.[AppTypeId]
                    ,A.[Port]
                    ,A.[InstallDirectory]
                FROM
                    [App] A
                ORDER BY
                    A.[Name]
                """)
            .ToListAsync(ct);

        var routes = rows
            .Where(r => AppTypeBehavior.IsRoutable(r.AppTypeId))
            .Select
            (
                r =>
                {
                    var proxyMode = AppTypeBehavior.ProxyMode(r.AppTypeId);
                    var domain = $"{r.Name}.{_settings.BaseDomain}";
                    var target = proxyMode == "reverse_proxy"
                        ? $"localhost:{r.Port}"
                        : r.InstallDirectory ?? "";

                    return new RouteEntry
                    (
                        r.ExternalId,
                        r.DisplayName,
                        domain,
                        target,
                        proxyMode,
                        Https: true
                    );
                }
            )
            .ToList();

        return CommandResult<RouteListResponse>.Success
        (
            new RouteListResponse(routes, _settings.BaseDomain)
        );
    }
}
