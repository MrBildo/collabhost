namespace Collabhost.Api.Authorization;

public class RequireRoleFilter(UserRole requiredRole) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync
    (
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next
    )
    {
        var currentUser = context.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

        return !HasSufficientRole(currentUser.Role)
            ? Results.Json
            (
                new { error = "Forbidden", message = $"This endpoint requires the {requiredRole} role." },
                statusCode: 403
            )
            : await next(context);
    }

    private bool HasSufficientRole(UserRole userRole) => userRole switch
    {
        UserRole.Administrator => true,
        UserRole.Agent => requiredRole == UserRole.Agent,
        _ => false
    };
}
