using System.Reflection;

using Collabhost.Api.Auth;
using Collabhost.Api.Data;
using Collabhost.Api.Data.Interceptors;
using Collabhost.Api.Features;
using Collabhost.Api.Services;

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
    ?? "Data Source=./db/collabhost.db";

var dbPath = connectionString.Replace("Data Source=", "");
var dbDir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
if (dbDir is not null)
{
    Directory.CreateDirectory(dbDir);
}

builder.Services.AddDbContext<CollabhostDbContext>
(
    options => options
        .UseSqlite(connectionString)
        .AddInterceptors(new AuditInterceptor())
);

// Auth
using var earlyLoggerFactory = LoggerFactory.Create(b => b.AddConsole());
var earlyLogger = earlyLoggerFactory.CreateLogger("Collabhost.Startup");
builder.Services.AddCollabhostAuth(builder.Configuration, earlyLogger);

// Feature modules (auto-discovered via reflection)
builder.Services.AddFeatureModules(Assembly.GetExecutingAssembly());

// Infrastructure services
builder.Services.AddScoped<PortAllocator>();

// OpenAPI
builder.Services.AddOpenApi();

// CORS (development only)
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors();
}

var app = builder.Build();

// Ensure database is created (dev only -- use migrations in prod)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CollabhostDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// Development middleware
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors
    (
        policy => policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader()
    );
}

// Auth middleware
app.UseCollabhostAuth();

// Feature module endpoints (auto-discovered)
app.MapFeatureModuleEndpoints();

// SPA fallback
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

// Default health/alive endpoints (from ServiceDefaults)
app.MapDefaultEndpoints();

app.Run();

// Make Program class accessible for WebApplicationFactory in tests
public partial class Program { }
