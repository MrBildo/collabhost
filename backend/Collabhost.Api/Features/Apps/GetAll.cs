using Collabhost.Api.Common;
using Collabhost.Api.Data;

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
        bool AutoStart
    );

    public static async Task<Results<Ok<List<Response>>, ProblemHttpResult>> HandleAsync
    (
        GetAllQueryHandler handler,
        CancellationToken ct
    )
    {
        var result = await handler.HandleAsync(ct);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.Problem(result.ErrorMessage, statusCode: 500);
    }
}

public class GetAllQueryHandler(CollabhostDbContext db)
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<QueryResult<List<GetAll.Response>>> HandleAsync(CancellationToken ct = default)
    {
        var results = await _db.Database
            .SqlQuery<GetAll.Response>(
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
                FROM
                    [App] A
                    INNER JOIN [AppType] AT ON AT.[Id] = A.[AppTypeId]
                ORDER BY
                    A.[Name]
                """)
            .ToListAsync(ct);

        return QueryResult<List<GetAll.Response>>.Success(results);
    }
}
