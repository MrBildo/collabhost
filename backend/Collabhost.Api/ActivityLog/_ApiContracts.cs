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
