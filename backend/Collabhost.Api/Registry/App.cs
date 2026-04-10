namespace Collabhost.Api.Registry;

public class App
{
    public Ulid Id { get; init; } = Ulid.NewUlid();

    public required string Slug { get; init; }

    public required string DisplayName { get; set; }

    public required Ulid AppTypeId { get; init; }

    // Not mapped to DB (Phase 1b). Populated at query time from the AppType
    // navigation property. Becomes a real column in Phase 2 when AppTypeId is removed.
    public string? AppTypeSlug { get; set; }

    public DateTime RegisteredAt { get; init; } = DateTime.UtcNow;

    public DateTime? ModifiedAt { get; set; }

    // Navigation property retained during Phase 1b coexistence so that AppTypeSlug
    // can be populated from AppType.Slug in AppStore queries.
    public AppType AppType { get; init; } = default!;
}
