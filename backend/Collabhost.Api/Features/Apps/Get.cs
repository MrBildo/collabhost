using Collabhost.Api.Common;
using Collabhost.Api.Data;

namespace Collabhost.Api.Features.Apps;

public static class Get
{
    public record Query(string ExternalId);

    public record Response
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
        bool AutoStart,
        DateTime RegisteredAt
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
        bool AutoStart,
        DateTime RegisteredAt,
        List<EnvironmentVariableResponse> EnvironmentVariables
    );

    public class Handler(CollabhostDbContext db)
    {
        private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));

        public async Task<QueryResult<DetailResponse>> HandleAsync(Query query, CancellationToken ct = default)
        {
            var result = await _db.Database
                .SqlQuery<Response>(
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
                        ,A.[AutoStart]
                        ,A.[RegisteredAt]
                    FROM
                        [App] A
                        INNER JOIN [AppType] AT ON AT.[Id] = A.[AppTypeId]
                        INNER JOIN [RestartPolicy] RP ON RP.[Id] = A.[RestartPolicyId]
                    WHERE
                        A.[ExternalId] = {query.ExternalId}
                    """)
                .SingleOrDefaultAsync(ct);

            if (result is null)
            {
                return QueryResult<DetailResponse>.Fail("App not found");
            }

            var envVars = await _db.Database
                .SqlQuery<EnvironmentVariableResponse>(
                    $"""
                    SELECT
                        EV.[Name]
                        ,EV.[Value]
                    FROM
                        [EnvironmentVariable] EV
                        INNER JOIN [App] A ON A.[Id] = EV.[AppId]
                    WHERE
                        A.[ExternalId] = {query.ExternalId}
                    ORDER BY
                        EV.[Name]
                    """)
                .ToListAsync(ct);

            var detail = new DetailResponse
            (
                result.ExternalId,
                result.Name,
                result.DisplayName,
                result.AppTypeName,
                result.InstallDirectory,
                result.CommandLine,
                result.Arguments,
                result.WorkingDirectory,
                result.RestartPolicyName,
                result.Port,
                result.HealthEndpoint,
                result.UpdateCommand,
                result.AutoStart,
                result.RegisteredAt,
                envVars
            );

            return QueryResult<DetailResponse>.Success(detail);
        }
    }

    public static async Task<Results<Ok<DetailResponse>, NotFound>> HandleAsync
    (
        string externalId,
        Handler handler,
        CancellationToken ct
    )
    {
        var result = await handler.HandleAsync(new Query(externalId), ct);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.NotFound();
    }
}
