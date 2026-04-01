using System.Text.Json.Nodes;

using Collabhost.Api.Domain.Entities;

namespace Collabhost.Api.Features.AppTypes;

public static class GetAll
{
    public record Response
    (
        string Id,
        string Name,
        string DisplayName,
        string? Description,
        bool IsBuiltIn,
        IReadOnlyDictionary<string, AppTypeCapabilityResponse> Capabilities
    );

    public static async Task<Ok<List<Response>>> HandleAsync
    (
        CommandDispatcher dispatcher,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(new GetAllAppTypesCommand(), cancellationToken);

        return TypedResults.Ok(result.Value!);
    }
}

public record GetAllAppTypesCommand : ICommand<List<GetAll.Response>>;

public sealed class GetAllAppTypesCommandHandler(CollabhostDbContext db)
    : ICommandHandler<GetAllAppTypesCommand, List<GetAll.Response>>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<CommandResult<List<GetAll.Response>>> HandleAsync
    (
        GetAllAppTypesCommand command,
        CancellationToken ct = default
    )
    {
        var appTypes = await _db.Set<AppType>()
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

        var allCapabilities = await _db.Database
            .SqlQuery<AppTypeCapabilityRow>
            (
                $"""
                SELECT
                    ATC.[AppTypeId]
                    ,C.[Slug] AS [CapabilitySlug]
                    ,C.[DisplayName] AS [CapabilityDisplayName]
                    ,C.[Category] AS [CapabilityCategory]
                    ,ATC.[Configuration]
                FROM
                    [AppTypeCapability] ATC
                    INNER JOIN [Capability] C ON C.[Id] = ATC.[CapabilityId]
                ORDER BY
                    C.[Category], C.[Slug]
                """
            )
            .ToListAsync(ct);

        // Group capabilities by AppTypeId — need to include it in the query
        var capabilitiesByType = await _db.Database
            .SqlQuery<AppTypeCapabilityWithTypeRow>
            (
                $"""
                SELECT
                    ATC.[AppTypeId]
                    ,C.[Slug] AS [CapabilitySlug]
                    ,C.[DisplayName] AS [CapabilityDisplayName]
                    ,C.[Category] AS [CapabilityCategory]
                    ,ATC.[Configuration]
                FROM
                    [AppTypeCapability] ATC
                    INNER JOIN [Capability] C ON C.[Id] = ATC.[CapabilityId]
                ORDER BY
                    C.[Category], C.[Slug]
                """
            )
            .ToListAsync(ct);

        var grouped = capabilitiesByType
            .GroupBy(c => c.AppTypeId)
            .ToDictionary
            (
                g => g.Key,
                g => AppTypeCapabilityMapper.BuildCapabilityDictionary
                (
                    g.Select
                    (
                        r => new AppTypeCapabilityRow
                        (
                            r.CapabilitySlug,
                            r.CapabilityDisplayName,
                            r.CapabilityCategory,
                            r.Configuration
                        )
                    )
                )
            );

        var results = appTypes
            .Select
            (
                t => new GetAll.Response
                (
                    t.ExternalId,
                    t.Name,
                    t.DisplayName,
                    t.Description,
                    t.IsBuiltIn,
                    grouped.TryGetValue(t.Id, out var caps) ? caps : new Dictionary<string, AppTypeCapabilityResponse>(StringComparer.Ordinal)
                )
            )
            .ToList();

        return CommandResult<List<GetAll.Response>>.Success(results);
    }
}

internal sealed record AppTypeCapabilityWithTypeRow
(
    Guid AppTypeId,
    string CapabilitySlug,
    string CapabilityDisplayName,
    string CapabilityCategory,
    string Configuration
);
