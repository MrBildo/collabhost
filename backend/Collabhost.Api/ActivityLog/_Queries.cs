namespace Collabhost.Api.ActivityLog;

public record ActivityEventQuery
(
    string? Category,
    string? AppSlug,
    string? ActorId,
    string? EventType,
    DateTime? Since,
    DateTime? Until,
    int Limit = 50,
    string? Cursor = null
);

public record ActivityEventPage
(
    IReadOnlyList<ActivityEvent> Items,
    string? NextCursor,
    bool HasMore
);
