using Microsoft.Extensions.Options;

namespace Collabhost.Api.Authorization;

public class AuthorizationSettings
{
    public const string SectionName = "Auth";

    public string? AdminKey { get; set; }
}

public class AuthorizationMiddleware
(
    RequestDelegate next,
    IOptionsMonitor<AuthorizationSettings> authorizationSettings,
    UserStore userStore,
    ILogger<AuthorizationMiddleware> logger
)
{
    private readonly RequestDelegate _next = next
        ?? throw new ArgumentNullException(nameof(next));

    private readonly IOptionsMonitor<AuthorizationSettings> _authorizationSettings = authorizationSettings
        ?? throw new ArgumentNullException(nameof(authorizationSettings));

    private readonly UserStore _userStore = userStore
        ?? throw new ArgumentNullException(nameof(userStore));

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

        var user = await ResolveUserAsync(userKey, context.RequestAborted);

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

    private async Task<User?> ResolveUserAsync(string authKey, CancellationToken ct)
    {
        var adminKey = _authorizationSettings.CurrentValue.AdminKey;

        // Config key bypass: permanent lockout override -- always works even if DB is empty
        if (adminKey is not null && authKey == adminKey)
        {
            var user = await _userStore.GetByAuthKeyAsync(authKey, ct);

            if (user is not null)
            {
                return user;
            }

            // DB has no user for the config key (deleted or first request before seed runs).
            // Create a transient admin identity so the request succeeds.
            _logger.LogWarning
            (
                "Auth bypass: request authenticated via config admin key with no matching DB user. "
                + "Create a proper user account."
            );

            return new User
            {
                Name = "Admin (config bypass)",
                AuthKey = authKey,
                Role = UserRole.Administrator,
            };
        }

        return await _userStore.GetByAuthKeyAsync(authKey, ct);
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

    private static bool ShouldSkip(string path, string method)
    {
        foreach (var prefix in _skipPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Status endpoint is public for read-only health monitoring
        return method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && path.Equals("/api/v1/status", StringComparison.OrdinalIgnoreCase);
    }
}
