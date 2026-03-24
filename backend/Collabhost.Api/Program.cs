using System.Reflection;

// --version flag
if (args.Contains("--version"))
{
    var version = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.1.0";
    Console.WriteLine($"Collabhost {version}");
    return;
}

var builder = WebApplication.CreateBuilder(args);

// Local config overlay
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Aspire service defaults (OpenTelemetry, health checks, service discovery)
builder.AddServiceDefaults();

// Database
var connectionString = builder.Configuration.GetConnectionString("Host")
    ?? "Data Source=./data/collabhost.db";

// Ensure data directory exists
var dbPath = connectionString.Replace("Data Source=", "");
var dbDir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
if (dbDir is not null)
{
    Directory.CreateDirectory(dbDir);
}

// OpenAPI
builder.Services.AddOpenApi();

// Health checks
builder.Services.AddHealthChecks();

// CORS (development only)
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors();
}

var app = builder.Build();

// Development middleware
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors(policy => policy
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader());
}

// API endpoints
var api = app.MapGroup("/api/v1");

api.MapGet("/status", () => Results.Ok(new
{
    status = "healthy",
    version = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.1.0",
    timestamp = DateTimeOffset.UtcNow
}));

// SPA fallback
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

// Default health/alive endpoints
app.MapDefaultEndpoints();

app.Run();
