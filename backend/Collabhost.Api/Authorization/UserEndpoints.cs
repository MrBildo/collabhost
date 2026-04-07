using Microsoft.AspNetCore.Http.HttpResults;

namespace Collabhost.Api.Authorization;

#pragma warning disable MA0076 // Ulid.ToString is not locale-sensitive
#pragma warning disable MA0011 // Ulid.ToString is not locale-sensitive
public static class UserEndpoints
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        var adminGroup = routes
            .MapGroup("/api/v1/users")
            .WithTags("Users")
            .AddEndpointFilter(new RequireRoleFilter(UserRole.Administrator));

        adminGroup.MapPost("/", CreateUserAsync);
        adminGroup.MapGet("/", ListUsersAsync);
        adminGroup.MapGet("/{id}", GetUserAsync);
        adminGroup.MapPatch("/{id}/deactivate", DeactivateUserAsync);

        var authGroup = routes.MapGroup("/api/v1/auth").WithTags("Auth");

        authGroup.MapGet("/me", GetMe);
    }

    private static async Task<IResult> CreateUserAsync
    (
        CreateUserRequest request,
        UserStore store,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.BadRequest(new { error = "Name is required." });
        }

        if (!Enum.IsDefined(request.Role))
        {
            return TypedResults.BadRequest(new { error = "Invalid role value." });
        }

        var user = await store.CreateAsync(request.Name, request.Role, ct);

        return TypedResults.Created
        (
            $"/api/v1/users/{user.Id}",
            new
            {
                id = user.Id.ToString(),
                name = user.Name,
                role = user.Role.ToString().ToLowerInvariant(),
                authKey = user.AuthKey,
                isActive = user.IsActive,
                createdAt = user.CreatedAt
            }
        );
    }

    private static async Task<IResult> ListUsersAsync
    (
        UserStore store,
        CancellationToken ct
    )
    {
        var users = await store.GetAllAsync(ct);

        var items = users
            .Select
            (
                u => new
                {
                    id = u.Id.ToString(),
                    name = u.Name,
                    role = u.Role.ToString().ToLowerInvariant(),
                    isActive = u.IsActive,
                    createdAt = u.CreatedAt
                }
            )
                .ToList();

        return TypedResults.Ok(items);
    }

    private static async Task<IResult> GetUserAsync
    (
        string id,
        UserStore store,
        CancellationToken ct
    )
    {
        if (!Ulid.TryParse(id, out var userId))
        {
            return TypedResults.NotFound();
        }

        var user = await store.GetByIdAsync(userId, ct);

        return user is null
            ? TypedResults.NotFound()
            : TypedResults.Ok
            (
                new
                {
                    id = user.Id.ToString(),
                    name = user.Name,
                    role = user.Role.ToString().ToLowerInvariant(),
                    isActive = user.IsActive,
                    createdAt = user.CreatedAt
                }
            );
    }

    private static async Task<IResult> DeactivateUserAsync
    (
        string id,
        UserStore store,
        CancellationToken ct
    )
    {
        if (!Ulid.TryParse(id, out var userId))
        {
            return TypedResults.NotFound();
        }

        var user = await store.GetByIdAsync(userId, ct);

        if (user is null)
        {
            return TypedResults.NotFound();
        }

        try
        {
            await store.DeactivateAsync(userId, ct);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Conflict(new { error = ex.Message });
        }

        var updated = await store.GetByIdAsync(userId, ct);

        return TypedResults.Ok
        (
            new
            {
                id = userId.ToString(),
                name = updated?.Name ?? user.Name,
                role = (updated?.Role ?? user.Role).ToString().ToLowerInvariant(),
                isActive = updated?.IsActive ?? false,
                createdAt = updated?.CreatedAt ?? user.CreatedAt
            }
        );
    }

    private static Ok<MeResponse> GetMe(ICurrentUser currentUser) =>
        TypedResults.Ok
        (
            new MeResponse
            (
                currentUser.UserId.ToString(),
                currentUser.User.Name,
                currentUser.Role.ToString().ToLowerInvariant()
            )
        );
}
#pragma warning restore MA0011
#pragma warning restore MA0076

internal record CreateUserRequest(string Name, UserRole Role);

internal record MeResponse(string Id, string Name, string Role);
