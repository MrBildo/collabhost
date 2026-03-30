namespace Collabhost.Api.Features.Apps;

public static class Get
{
    internal sealed record Row
    (
        string ExternalId,
        string Name,
        string DisplayName,
        string AppTypeName,
        string InstallDirectory,
        int? Port,
        DateTime RegisteredAt,
        Guid AppTypeId
    );

    public record DetailResponse
    (
        string ExternalId,
        string Name,
        string DisplayName,
        string AppTypeName,
        string InstallDirectory,
        int? Port,
        DateTime RegisteredAt
    );

    public static async Task<Results<Ok<DetailResponse>, NotFound>> HandleAsync
    (
        string externalId,
        CommandDispatcher dispatcher,
        CancellationToken ct
    )
    {
        var result = await dispatcher.DispatchAsync(new GetAppCommand(externalId), ct);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.NotFound();
    }
}

public record GetAppCommand(string ExternalId) : ICommand<Get.DetailResponse>;

public sealed class GetAppCommandHandler(CollabhostDbContext db) : ICommandHandler<GetAppCommand, Get.DetailResponse>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<CommandResult<Get.DetailResponse>> HandleAsync(GetAppCommand command, CancellationToken ct = default)
    {
        var row = await _db.Database
            .SqlQuery<Get.Row>(
                $"""
                SELECT
                    A.[ExternalId]
                    ,A.[Name]
                    ,A.[DisplayName]
                    ,AT.[DisplayName] AS [AppTypeName]
                    ,A.[InstallDirectory]
                    ,A.[Port]
                    ,A.[RegisteredAt]
                    ,A.[AppTypeId]
                FROM
                    [App] A
                    INNER JOIN [AppType] AT ON AT.[Id] = A.[AppTypeId]
                WHERE
                    A.[ExternalId] = {command.ExternalId}
                """)
            .SingleOrDefaultAsync(ct);

        if (row is null)
        {
            return CommandResult<Get.DetailResponse>.Fail("NOT_FOUND", "App not found");
        }

        var detail = new Get.DetailResponse
        (
            row.ExternalId,
            row.Name,
            row.DisplayName,
            row.AppTypeName,
            row.InstallDirectory,
            row.Port,
            row.RegisteredAt
        );

        return CommandResult<Get.DetailResponse>.Success(detail);
    }
}
