namespace Collabhost.Api.ActivityLog;

public record ActivityEventItem
(
    string Id,
    string EventType,
    string ActorId,
    string ActorName,
    string? AppId,
    string? AppSlug,
    JsonElement? Metadata,
    DateTime Timestamp,
    string Severity
);

public record ActivityEventListResponse
(
    IReadOnlyList<ActivityEventItem> Items,
    string? NextCursor,
    bool HasMore
);

public static class ActivityLogEndpoints
{
    // Route group reserved for Phase 4 (full events API)
    public static void Map(IEndpointRouteBuilder routes) =>
        routes.MapGroup("/api/v1/events")
            .WithTags("ActivityLog");
}
