namespace Collabhost.Api.Registry;

public sealed class App
{
    public Ulid Id { get; init; } = Ulid.NewUlid();

    public required string Slug { get; init; }

    public required string DisplayName { get; set; }

    public required Ulid AppTypeId { get; init; }

    public DateTime RegisteredAt { get; init; } = DateTime.UtcNow;

    public DateTime? ModifiedAt { get; set; }

    // Navigation (loaded when needed, not eagerly)
    public AppType? AppType { get; init; }
}
