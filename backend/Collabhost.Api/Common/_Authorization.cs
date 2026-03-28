using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Collabhost.Api.Auth;

public class AuthorizationSettings
{
    public string? AdminKey { get; set; }
}

public class ApiKeyAuthorizationMiddleware
(
    RequestDelegate next,
    IOptionsMonitor<AuthorizationSettings> authSettings,
    ILogger<ApiKeyAuthorizationMiddleware> logger
)
{
    private readonly RequestDelegate _next = next ?? throw new ArgumentNullException(nameof(next));
    private readonly IOptionsMonitor<AuthorizationSettings> _authSettings = authSettings ?? throw new ArgumentNullException(nameof(authSettings));
    private readonly ILogger<ApiKeyAuthorizationMiddleware> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

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
        var adminKey = _authSettings.CurrentValue.AdminKey;

        if (adminKey is null || userKey != adminKey)
        {
            _logger.LogWarning("Auth rejected for {Path} — key {Status}", path, userKey is null ? "missing" : "invalid");

            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";

            var body = new { error = "Forbidden", message = "Invalid or missing API key." };
            await context.Response.WriteAsync(JsonSerializer.Serialize(body, _jsonOptions));
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

        return method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && path.Equals("/api/v1/status", StringComparison.OrdinalIgnoreCase);
    }
}

public static class AuthorizationExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddCollabhostAuth
        (
            IConfiguration configuration,
            ILogger logger
        )
        {
            services.Configure<AuthorizationSettings>(configuration.GetSection("Auth"));

            var generatedKey = Ulid.NewUlid().ToString(null, CultureInfo.InvariantCulture);

            services.PostConfigure<AuthorizationSettings>
            (
                s =>
                {
                    if (s.AdminKey is not null)
                    {
                        return;
                    }

                    s.AdminKey = generatedKey;

                    logger.LogWarning
                    (
                        "No Auth:AdminKey configured. Generated temporary key: {AdminKey}",
                        generatedKey
                    );
                }
            );

            return services;
        }
    }

    extension(IApplicationBuilder app)
    {
        public IApplicationBuilder UseCollabhostAuth()
        {
            app.UseMiddleware<ApiKeyAuthorizationMiddleware>();
            return app;
        }
    }
}

