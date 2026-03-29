using Collabhost.Api.Domain;

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
        string CommandLine,
        string? Arguments,
        string? WorkingDirectory,
        string RestartPolicyName,
        int? Port,
        string? HealthEndpoint,
        string? UpdateCommand,
        int? UpdateTimeoutSeconds,
        bool AutoStart,
        DateTime RegisteredAt,
        Guid AppTypeId
    );

    public record EnvironmentVariableResponse(string Name, string Value);

    public record DetailResponse
    (
        string ExternalId,
        string Name,
        string DisplayName,
        string AppTypeName,
        string InstallDirectory,
        string CommandLine,
        string? Arguments,
        string? WorkingDirectory,
        string RestartPolicyName,
        int? Port,
        string? HealthEndpoint,
        string? UpdateCommand,
        int? UpdateTimeoutSeconds,
        bool AutoStart,
        DateTime RegisteredAt,
        bool IsProtected,
        IReadOnlyList<EnvironmentVariableResponse> EnvironmentVariables
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

#pragma warning disable MA0051 // Long method justified — multi-query SQL projection
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
                    ,A.[CommandLine]
                    ,A.[Arguments]
                    ,A.[WorkingDirectory]
                    ,RP.[DisplayName] AS [RestartPolicyName]
                    ,A.[Port]
                    ,A.[HealthEndpoint]
                    ,A.[UpdateCommand]
                    ,A.[UpdateTimeoutSeconds]
                    ,A.[AutoStart]
                    ,A.[RegisteredAt]
                    ,A.[AppTypeId]
                FROM
                    [App] A
                    INNER JOIN [AppType] AT ON AT.[Id] = A.[AppTypeId]
                    INNER JOIN [RestartPolicy] RP ON RP.[Id] = A.[RestartPolicyId]
                WHERE
                    A.[ExternalId] = {command.ExternalId}
                """)
            .SingleOrDefaultAsync(ct);

        if (row is null)
        {
            return CommandResult<Get.DetailResponse>.Fail("NOT_FOUND", "App not found");
        }

        var environmentVariables = await _db.Database
            .SqlQuery<Get.EnvironmentVariableResponse>(
                $"""
                SELECT
                    EV.[Name]
                    ,EV.[Value]
                FROM
                    [EnvironmentVariable] EV
                    INNER JOIN [App] A ON A.[Id] = EV.[AppId]
                WHERE
                    A.[ExternalId] = {command.ExternalId}
                ORDER BY
                    EV.[Name]
                """)
            .ToListAsync(ct);

        var detail = new Get.DetailResponse
        (
            row.ExternalId,
            row.Name,
            row.DisplayName,
            row.AppTypeName,
            row.InstallDirectory,
            row.CommandLine,
            row.Arguments,
            row.WorkingDirectory,
            row.RestartPolicyName,
            row.Port,
            row.HealthEndpoint,
            row.UpdateCommand,
            row.UpdateTimeoutSeconds,
            row.AutoStart,
            row.RegisteredAt,
            AppTypeBehavior.IsProtected(row.AppTypeId),
            environmentVariables
        );

        return CommandResult<Get.DetailResponse>.Success(detail);
    }
#pragma warning restore MA0051
}
