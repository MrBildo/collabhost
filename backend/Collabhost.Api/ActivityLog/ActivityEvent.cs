namespace Collabhost.Api.ActivityLog;

public class ActivityEvent
{
    public Ulid Id { get; init; } = Ulid.NewUlid();

    public required string EventType { get; init; }

    public required string ActorId { get; init; }

    public required string ActorName { get; init; }

    public string? AppId { get; init; }

    public string? AppSlug { get; init; }

    public string? MetadataJson { get; init; }

    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
