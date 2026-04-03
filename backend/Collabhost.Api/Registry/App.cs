namespace Collabhost.Api.Registry;

public class App
{
    public Ulid Id { get; init; } = Ulid.NewUlid();

    public required string Slug { get; init; }

    public required string DisplayName { get; set; }

    public required Ulid AppTypeId { get; init; }

    public DateTime RegisteredAt { get; init; } = DateTime.UtcNow;

    public DateTime? ModifiedAt { get; set; }

    public AppType? AppType { get; init; }
}
