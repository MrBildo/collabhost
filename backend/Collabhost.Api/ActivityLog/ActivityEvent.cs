using Collabhost.Api.Shared;

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

    // System-actor convenience factory (#109). The supervisor, proxy manager, seeder, and user-seed
    // service all record System-stamped events with the same actor boilerplate; this collapses that
    // to one call. Operation leaves never use it -- they record via the Operation base RecordAsync,
    // which stamps the acting user (see OperationSpineTests). appId is optional for app-less events
    // (e.g. user.seeded).
    public static ActivityEvent ForSystem
    (
        string eventType,
        Ulid? appId = null,
        string? appSlug = null,
        string? metadataJson = null
    ) =>
        new()
        {
            EventType = eventType,
            ActorId = ActivityActor.SystemId,
            ActorName = ActivityActor.SystemName,
            AppId = appId?.ToCanonicalString(),
            AppSlug = appSlug,
            MetadataJson = metadataJson
        };
}
