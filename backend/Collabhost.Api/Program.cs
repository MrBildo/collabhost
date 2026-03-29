using System.Reflection;

using Collabhost.Api.Auth;

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
builder.Services.AddCollabhostDatabase(builder.Configuration);

// Auth
using var earlyLoggerFactory = LoggerFactory.Create(b => b.AddConsole());
var earlyLogger = earlyLoggerFactory.CreateLogger("Collabhost.Startup");
builder.Services.AddCollabhostAuth(builder.Configuration, earlyLogger);

// Feature modules (auto-discovered via reflection)
builder.Services.AddFeatureModules(Assembly.GetExecutingAssembly());

// Command dispatcher (auto-discovers ICommandHandler<,> implementations)
builder.Services.AddCommandDispatcher();

// Infrastructure services
builder.Services.AddInfrastructureServices();

// Proxy services
builder.Services.AddProxyServices(builder.Configuration);

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
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<CollabhostDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// Seed proxy app from configuration (idempotent)
await SeedProxyAppAsync(app);

static async Task SeedProxyAppAsync(WebApplication application)
{
    await using var scope = application.Services.CreateAsyncScope();
    var seeder = scope.ServiceProvider.GetRequiredService<IProxyAppSeeder>();
    await seeder.SeedAsync(CancellationToken.None);
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

await app.RunAsync();
