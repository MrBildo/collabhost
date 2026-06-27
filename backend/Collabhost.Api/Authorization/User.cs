namespace Collabhost.Api.Authorization;

public class User
{
    // ULID string representation length. Single source for the AuthKey column's MaxLength
    // (UserConfiguration) and the admin-key seed-time shape validation (UserSeedService), so
    // the persisted schema constraint and the seed-time check can never drift apart.
    public const int AuthKeyMaxLength = 26;

    public Ulid Id { get; init; } = Ulid.NewUlid();

    public required string Name { get; set; }

    public required string AuthKey { get; init; }

    public UserRole Role { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
