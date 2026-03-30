using Collabhost.Api.Domain;

namespace Collabhost.Api.Features.Apps;

public static class GetAll
{
    public record Response
    (
        string ExternalId,
        string Name,
        string DisplayName,
        string AppTypeName,
        int? Port,
        string? UpdateCommand,
        int? UpdateTimeoutSeconds,
        bool AutoStart,
        bool IsProtected,
        bool IsRoutable
    );

    internal sealed record Row
    (
        string ExternalId,
        string Name,
        string DisplayName,
        string AppTypeName,
        int? Port,
        string? UpdateCommand,
        int? UpdateTimeoutSeconds,
        bool AutoStart,
        Guid AppTypeId
    );

    public static async Task<Results<Ok<List<Response>>, ProblemHttpResult>> HandleAsync
    (
        CommandDispatcher dispatcher,
        CancellationToken ct
    )
    {
        var result = await dispatcher.DispatchAsync(new GetAllAppsCommand(), ct);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.Problem(result.ErrorMessage, statusCode: 500);
    }
}

public record GetAllAppsCommand : ICommand<List<GetAll.Response>>;

public sealed class GetAllAppsCommandHandler(CollabhostDbContext db) : ICommandHandler<GetAllAppsCommand, List<GetAll.Response>>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<CommandResult<List<GetAll.Response>>> HandleAsync(GetAllAppsCommand command, CancellationToken ct = default)
    {
        var rows = await _db.Database
            .SqlQuery<GetAll.Row>(
                $"""
                SELECT
                    A.[ExternalId]
                    ,A.[Name]
                    ,A.[DisplayName]
                    ,AT.[DisplayName] AS [AppTypeName]
                    ,A.[Port]
                    ,A.[UpdateCommand]
                    ,A.[UpdateTimeoutSeconds]
                    ,A.[AutoStart]
                    ,A.[AppTypeId]
                FROM
                    [App] A
                    INNER JOIN [AppType] AT ON AT.[Id] = A.[AppTypeId]
                ORDER BY
                    A.[Name]
                """)
            .ToListAsync(ct);

        var results = rows
            .Select(
                row => new GetAll.Response
                (
                    row.ExternalId,
                    row.Name,
                    row.DisplayName,
                    row.AppTypeName,
                    row.Port,
                    row.UpdateCommand,
                    row.UpdateTimeoutSeconds,
                    row.AutoStart,
                    AppTypeBehavior.IsProtected(row.AppTypeId),
                    AppTypeBehavior.IsRoutable(row.AppTypeId)
                ))
            .ToList();

        return CommandResult<List<GetAll.Response>>.Success(results);
    }
}
