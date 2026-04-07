namespace Collabhost.Api.Authorization;

public class RequireRoleFilter(UserRole requiredRole) : IEndpointFilter
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async ValueTask<object?> InvokeAsync
    (
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next
    )
    {
        var currentUser = context.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

        if (!HasSufficientRole(currentUser.Role))
        {
            context.HttpContext.Response.StatusCode = 403;
            context.HttpContext.Response.ContentType = "application/json";

            var body = new
            {
                error = "Forbidden",
                message = $"This endpoint requires the {requiredRole} role."
            };

            await context.HttpContext.Response.WriteAsync
            (
                JsonSerializer.Serialize(body, _jsonOptions),
                context.HttpContext.RequestAborted
            );

            return null;
        }

        return await next(context);
    }

    private bool HasSufficientRole(UserRole userRole) => userRole switch
    {
        UserRole.Administrator => true,
        UserRole.Agent => requiredRole == UserRole.Agent,
        _ => false
    };
}
