namespace Collabhost.Api.Authorization;

public class AuthorizationSettings
{
    public const string SectionName = "Auth";

    // Mutable by design, NOT an oversight. AddCollabhostAuthorization (_Registration.cs) applies
    // the COLLABHOST_ADMIN_KEY env-over-config override via a PostConfigure delegate that assigns
    // this property on the already-constructed options instance (same pattern as the other
    // COLLABHOST_* path env vars). A PostConfigure delegate cannot assign an init-only setter
    // (CS8852), so { get; init; } would break the env override -- the setter is load-bearing here.
    public string? AdminKey { get; set; }
}

public class AuthorizationMiddleware
(
    RequestDelegate next,
    AuthKeyResolver authKeyResolver,
    ILogger<AuthorizationMiddleware> logger
)
{
    private readonly RequestDelegate _next = next
        ?? throw new ArgumentNullException(nameof(next));

    private readonly AuthKeyResolver _authKeyResolver = authKeyResolver
        ?? throw new ArgumentNullException(nameof(authKeyResolver));

    private readonly ILogger<AuthorizationMiddleware> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    private static readonly string[] _skipPrefixes = ["/health", "/alive", "/openapi", "/mcp"];

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (ShouldSkip(path, context.Request.Method))
        {
            await _next(context);
            return;
        }

        var userKey = context.Request.Headers["X-User-Key"].FirstOrDefault();

        // Query param auth is only allowed for SSE endpoints where EventSource
        // cannot set custom headers (browser limitation)
        if (userKey is null && path.EndsWith("/logs/stream", StringComparison.OrdinalIgnoreCase))
        {
            userKey = context.Request.Query["key"].FirstOrDefault();
        }

        if (string.IsNullOrEmpty(userKey))
        {
            await WriteUnauthorizedAsync(context, "API key is required. Provide X-User-Key header.");
            return;
        }

        var user = await _authKeyResolver.ResolveAsync(userKey, context.RequestAborted);

        if (user is null)
        {
            _logger.LogWarning("Auth rejected for {Path} -- invalid key", path);

            await WriteUnauthorizedAsync(context, "Invalid API key.");
            return;
        }

        if (!user.IsActive)
        {
            _logger.LogWarning
            (
                "Auth rejected for {Path} -- user {UserName} ({UserId}) is deactivated",
                path,
                user.Name,
                user.Id
            );

            await WriteUnauthorizedAsync(context, "User account is deactivated.");
            return;
        }

        var currentUser = context.RequestServices.GetRequiredService<CurrentUser>();

        currentUser.Set(user);

        await _next(context);
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context, string message)
    {
        context.Response.StatusCode = 401;
        context.Response.ContentType = "application/json";

        var body = new { error = "Unauthorized", message };

        await context.Response.WriteAsync
        (
            JsonSerializer.Serialize(body, _jsonOptions),
            context.RequestAborted
        );
    }

    // Internal for direct unit testing of the segment-match contract (SVC-02). A raw
    // StartsWith would let "/healthz", "/health-secrets", "/aliveness" slip past the
    // auth wall because they share a prefix with a skip path; match on whole path
    // segments instead -- a prefix matches only when the path IS the prefix or
    // continues at a "/" boundary.
    internal static bool ShouldSkip(string path, string method)
    {
        foreach (var prefix in _skipPrefixes)
        {
            if (IsPathSegmentMatch(path, prefix))
            {
                return true;
            }
        }

        // Status and version endpoints are public for read-only health monitoring and build identity
        return method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && (path.Equals("/api/v1/status", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/api/v1/version", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPathSegmentMatch(string path, string prefix) =>
        path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
        || (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && path.Length > prefix.Length
            && path[prefix.Length] == '/');
}
