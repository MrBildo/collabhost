namespace Collabhost.Api.Authorization;

public class User
{
    public Ulid Id { get; init; } = Ulid.NewUlid();

    public required string Name { get; set; }

    public required string AuthKey { get; init; }

    public UserRole Role { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
