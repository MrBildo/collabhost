using System.Text.Json;
using System.Text.Json.Nodes;

using Collabhost.Api.Domain.Capabilities;
using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Domain.Entities;

namespace Collabhost.Api.Features.Apps;

public interface ICapabilityBridge
{
    Task<List<ResolvedCapabilityData>> ResolveAllCapabilitiesAsync
    (
        Guid appId,
        Guid appTypeId,
        CancellationToken ct
    );

    RoutingConfiguration? ExtractRoutingConfiguration
    (
        List<ResolvedCapabilityData> resolvedCapabilities
    );
}

internal sealed class CapabilityBridge(CollabhostDbContext db) : ICapabilityBridge
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<List<ResolvedCapabilityData>> ResolveAllCapabilitiesAsync
    (
        Guid appId,
        Guid appTypeId,
        CancellationToken ct
    )
    {
        var typeCapabilities = await _db.Database
            .SqlQuery<TypeCapabilityRow>
            (
                $"""
                SELECT
                    ATC.[Id] AS [AppTypeCapabilityId]
                    ,C.[Slug]
                    ,C.[DisplayName]
                    ,C.[Category]
                    ,ATC.[Configuration] AS [DefaultConfiguration]
                FROM
                    [AppTypeCapability] ATC
                    INNER JOIN [Capability] C ON C.[Id] = ATC.[CapabilityId]
                WHERE
                    ATC.[AppTypeId] = {appTypeId}
                ORDER BY
                    C.[Category], C.[Slug]
                """
            )
            .ToListAsync(ct);

        var overrides = await _db.Set<CapabilityConfiguration>()
            .AsNoTracking()
            .Where(cc => cc.AppId == appId)
            .ToListAsync(ct);


        var overrideLookup = overrides.ToDictionary(o => o.AppTypeCapabilityId);

        var results = new List<ResolvedCapabilityData>();

        foreach (var typeCapability in typeCapabilities)
        {
            var hasOverride = overrideLookup.TryGetValue(typeCapability.AppTypeCapabilityId, out var overrideRow);

            string resolvedJson;

            if (hasOverride && overrideRow is not null)
            {
                // Find the capability ID to pass to MergeJson
                var capabilityId = await _db.Set<AppTypeCapability>()
                    .AsNoTracking()
                    .Where(atc => atc.Id == typeCapability.AppTypeCapabilityId)
                    .Select(atc => atc.CapabilityId)
                    .SingleAsync(ct);

                resolvedJson = CapabilityResolver.MergeJson
                (
                    typeCapability.DefaultConfiguration,
                    overrideRow.Configuration,
                    capabilityId
                );
            }
            else
            {
                resolvedJson = typeCapability.DefaultConfiguration;
            }

            results.Add(new ResolvedCapabilityData
            (
                typeCapability.Slug,
                typeCapability.DisplayName,
                typeCapability.Category,
                resolvedJson,
                hasOverride
            ));
        }

        return results;
    }

    public RoutingConfiguration? ExtractRoutingConfiguration
    (
        List<ResolvedCapabilityData> resolvedCapabilities
    )
    {
        var routingData = resolvedCapabilities
            .SingleOrDefault
            (
                c => string.Equals(c.Slug, StringCatalog.Capabilities.Routing, StringComparison.Ordinal)
            );

        if (routingData is null)
        {
            return null;
        }

        return JsonSerializer.Deserialize<RoutingConfiguration>(routingData.ResolvedConfiguration, _jsonOptions);
    }
}

internal sealed record TypeCapabilityRow
(
    Guid AppTypeCapabilityId,
    string Slug,
    string DisplayName,
    string Category,
    string DefaultConfiguration
);
