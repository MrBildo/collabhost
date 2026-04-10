namespace Collabhost.Api.Registry;

public class App
{
    public Ulid Id { get; init; } = Ulid.NewUlid();

    public required string Slug { get; init; }

    public required string DisplayName { get; set; }

    public required Ulid AppTypeId { get; init; }

    // Phase 1a coexistence: not mapped to DB (no migration yet). Populated from
    // AppType.Slug via navigation property. Becomes a real column in Phase 2.
    public string? AppTypeSlug { get; set; }

    public DateTime RegisteredAt { get; init; } = DateTime.UtcNow;

    public DateTime? ModifiedAt { get; set; }

    public AppType AppType { get; init; } = default!;
}
