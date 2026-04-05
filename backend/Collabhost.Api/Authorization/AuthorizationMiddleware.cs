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
    ILogger<AuthorizationMiddleware> logger
)
{
    private readonly RequestDelegate _next = next
        ?? throw new ArgumentNullException(nameof(next));

    private readonly IOptionsMonitor<AuthorizationSettings> _authorizationSettings = authorizationSettings
        ?? throw new ArgumentNullException(nameof(authorizationSettings));

    private readonly ILogger<AuthorizationMiddleware> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    private static readonly string[] _skipPrefixes = ["/health", "/alive", "/openapi"];

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

        var adminKey = _authorizationSettings.CurrentValue.AdminKey;

        if (adminKey is null || userKey != adminKey)
        {
            _logger.LogWarning
            (
                "Auth rejected for {Path} -- key {Status}",
                path,
                userKey is null ? "missing" : "invalid"
            );

            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";

            var body = new { error = "Forbidden", message = "Invalid or missing API key." };

            await context.Response.WriteAsync
            (
                JsonSerializer.Serialize(body, _jsonOptions),
                context.RequestAborted
            );

            return;
        }

        await _next(context);
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
