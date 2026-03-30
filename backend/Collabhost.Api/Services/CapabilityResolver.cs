using System.Text.Json;
using System.Text.Json.Nodes;

using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Domain.Entities;

namespace Collabhost.Api.Services;

public interface ICapabilityResolver
{
    Task<T?> ResolveAsync<T>(Guid appId, Guid capabilityId, CancellationToken ct = default) where T : class;
}

public sealed class CapabilityResolver(IServiceScopeFactory scopeFactory) : ICapabilityResolver
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IServiceScopeFactory _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));

    public async Task<T?> ResolveAsync<T>(Guid appId, Guid capabilityId, CancellationToken ct = default) where T : class
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CollabhostDbContext>();

        // Get the app's type
        var app = await db.Apps
            .AsNoTracking()
            .Where(a => a.Id == appId)
            .Select(a => new { a.AppTypeId })
            .SingleOrDefaultAsync(ct);

        if (app is null)
        {
            return null;
        }

        // Find the AppTypeCapability for this (AppType, Capability) pair
        var typeCapability = await db.Set<AppTypeCapability>()
            .AsNoTracking()
            .Where(atc => atc.AppTypeId == app.AppTypeId && atc.CapabilityId == capabilityId)
            .SingleOrDefaultAsync(ct);

        if (typeCapability is null)
        {
            return null;
        }

        // Check for per-app override
        var instanceOverride = await db.Set<CapabilityConfiguration>()
            .AsNoTracking()
            .Where(cc => cc.AppId == appId && cc.AppTypeCapabilityId == typeCapability.Id)
            .SingleOrDefaultAsync(ct);

        if (instanceOverride is null)
        {
            // No override — return type defaults as-is
            return DeserializeOrThrow<T>(typeCapability.Configuration, capabilityId);
        }

        // Merge override on top of type defaults
        var merged = MergeJson(typeCapability.Configuration, instanceOverride.Configuration, capabilityId);
        return DeserializeOrThrow<T>(merged, capabilityId);
    }

    private static T DeserializeOrThrow<T>(string json, Guid capabilityId) where T : class =>
        JsonSerializer.Deserialize<T>(json, _jsonOptions)
            ?? throw new InvalidOperationException(
                $"Failed to deserialize capability configuration for capability {capabilityId}. " +
                $"JSON: {json}");

    internal static string MergeJson(string defaultsJson, string overrideJson, Guid capabilityId)
    {
        var defaults = JsonNode.Parse(defaultsJson)?.AsObject()
            ?? throw new InvalidOperationException(
                $"Invalid type-level configuration JSON for capability {capabilityId}");

        var overrides = JsonNode.Parse(overrideJson)?.AsObject()
            ?? throw new InvalidOperationException(
                $"Invalid override configuration JSON for capability {capabilityId}");

        var isEnvironmentDefaults = capabilityId == IdentifierCatalog.Capabilities.EnvironmentDefaults;

        foreach (var property in overrides)
        {
            if (isEnvironmentDefaults
                && string.Equals(property.Key, "defaults", StringComparison.OrdinalIgnoreCase)
                && property.Value is JsonObject overrideDict
                && defaults[property.Key] is JsonObject defaultDict)
            {
                // Dictionary merge: per-key within the defaults dictionary
                foreach (var entry in overrideDict)
                {
                    defaultDict[entry.Key] = entry.Value?.DeepClone();
                }
            }
            else
            {
                // Shallow merge: override replaces entirely
                defaults[property.Key] = property.Value?.DeepClone();
            }
        }

        return defaults.ToJsonString(_jsonOptions);
    }
}
