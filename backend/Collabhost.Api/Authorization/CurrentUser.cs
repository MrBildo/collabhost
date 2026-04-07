namespace Collabhost.Api.Authorization;

public class CurrentUser : ICurrentUser
{
#pragma warning disable IDE0032 // Backing field is nullable; property throws on null access -- not auto-property eligible
    private User? _user;
#pragma warning restore IDE0032

    public User User => _user
        ?? throw new InvalidOperationException("User not resolved. Ensure auth has run.");

    public Ulid UserId => User.Id;

    public UserRole Role => User.Role;

    public bool IsAdministrator => Role == UserRole.Administrator;

    public void Set(User user) => _user = user;
}
