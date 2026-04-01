using System.Text.Json.Nodes;

namespace Collabhost.Api.Features.AppTypes;

public record AppTypeCapabilityResponse
(
    string Category,
    string DisplayName,
    JsonObject Defaults
);

internal static class AppTypeCapabilityMapper
{
    internal static IReadOnlyDictionary<string, AppTypeCapabilityResponse> BuildCapabilityDictionary
    (
        IEnumerable<AppTypeCapabilityRow> rows
    )
    {
        var result = new Dictionary<string, AppTypeCapabilityResponse>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var defaults = JsonNode.Parse(row.Configuration)?.AsObject() ?? [];

            result[row.CapabilitySlug] = new AppTypeCapabilityResponse
            (
                row.CapabilityCategory,
                row.CapabilityDisplayName,
                defaults
            );
        }

        return result;
    }
}

internal sealed record AppTypeCapabilityRow
(
    string CapabilitySlug,
    string CapabilityDisplayName,
    string CapabilityCategory,
    string Configuration
);
