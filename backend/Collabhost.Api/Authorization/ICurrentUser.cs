namespace Collabhost.Api.Authorization;

public interface ICurrentUser
{
    User User { get; }

    Ulid UserId { get; }

    UserRole Role { get; }

    bool IsAdministrator { get; }
}
