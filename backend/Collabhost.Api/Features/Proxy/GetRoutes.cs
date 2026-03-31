using System.Text.Json;

namespace Collabhost.Api.Features.Proxy;

public static class GetRoutes
{
    public record RouteQueryRow
    (
        string ExternalId,
        string Name,
        string DisplayName,
        string? ServeMode
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

#pragma warning disable MA0051 // Long method justified — SQL query with JSON parsing
    public async Task<CommandResult<RouteListResponse>> HandleAsync(GetRoutesCommand command, CancellationToken ct = default)
    {
        var rows = await _db.Database
            .SqlQuery<GetRoutes.RouteQueryRow>(
                $"""
                SELECT
                    A.[ExternalId]
                    ,A.[Name]
                    ,A.[DisplayName]
                    ,ATC.[Configuration] AS [ServeMode]
                FROM
                    [App] A
                    INNER JOIN [AppTypeCapability] ATC ON ATC.[AppTypeId] = A.[AppTypeId]
                    INNER JOIN [Capability] C ON C.[Id] = ATC.[CapabilityId]
                WHERE
                    C.[Slug] = 'routing'
                ORDER BY
                    A.[Name]
                """)
            .ToListAsync(ct);

        var routes = rows
            .Select
            (
                r =>
                {
                    var serveMode = ExtractServeMode(r.ServeMode);
                    var domain = $"{r.Name}.{_settings.BaseDomain}";

                    return new RouteEntry
                    (
                        r.ExternalId,
                        r.DisplayName,
                        domain,
                        serveMode ?? "unknown",
                        serveMode ?? "unknown",
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
#pragma warning restore MA0051

    private static string? ExtractServeMode(string? routingConfiguration)
    {
        if (string.IsNullOrWhiteSpace(routingConfiguration))
        {
            return null;
        }

        try
        {
            var doc = JsonDocument.Parse(routingConfiguration);
            if (doc.RootElement.TryGetProperty("serveMode", out var serveModeElement))
            {
                return serveModeElement.GetString();
            }
        }
        catch (JsonException)
        {
            // Invalid JSON
        }

        return null;
    }
}
