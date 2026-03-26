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
                .SqlQuery<Response>($"""
                    SELECT
                        a.ExternalId,
                        a.Name,
                        a.DisplayName,
                        at.DisplayName AS AppTypeName,
                        a.InstallDirectory,
                        a.CommandLine,
                        a.Arguments,
                        a.WorkingDirectory,
                        rp.DisplayName AS RestartPolicyName,
                        a.Port,
                        a.HealthEndpoint,
                        a.UpdateCommand,
                        a.AutoStart,
                        a.RegisteredAt
                    FROM Apps a
                    INNER JOIN AppTypes at ON a.AppTypeId = at.Id
                    INNER JOIN RestartPolicies rp ON a.RestartPolicyId = rp.Id
                    WHERE a.ExternalId = {query.ExternalId}
                    """)
                .FirstOrDefaultAsync(ct);

            if (result is null)
            {
                return QueryResult<DetailResponse>.Fail("App not found");
            }

            // Fetch env vars separately (can't nest via SqlQuery)
            var envVars = await _db.Database
                .SqlQuery<EnvironmentVariableResponse>($"""
                    SELECT ev.Name, ev.Value
                    FROM EnvironmentVariables ev
                    INNER JOIN Apps a ON ev.AppId = a.Id
                    WHERE a.ExternalId = {query.ExternalId}
                    ORDER BY ev.Name
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
