using Collabhost.Api.Authorization;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Dashboard;
using Collabhost.Api.Data;
using Collabhost.Api.Events;
using Collabhost.Api.Filesystem;
using Collabhost.Api.Platform;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;
using Collabhost.Api.Supervisor;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Aspire service defaults
builder.AddServiceDefaults();

// Database
builder.Services.AddDataAccess(builder.Configuration);

// Memory cache
builder.Services.AddMemoryCache();

// Auth
using var earlyLoggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
var earlyLogger = earlyLoggerFactory.CreateLogger("Startup");

builder.Services.AddCollabhostAuthorization(builder.Configuration, earlyLogger);

// Subsystems
builder.Services.AddRegistry();
builder.Services.AddCapabilities();
builder.Services.AddEventBus();
builder.Services.AddSupervisor();
builder.Services.AddProxy(builder.Configuration);

// OpenAPI
builder.Services.AddOpenApi();

// CORS (dev only)
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors();
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

    await using var context = await db.CreateDbContextAsync();
    await context.Database.MigrateAsync();

    var proxySeeder = scope.ServiceProvider.GetRequiredService<ProxyAppSeeder>();
    await proxySeeder.SeedAsync(CancellationToken.None);

    app.MapOpenApi();
    app.UseCors(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
}

// Middleware
app.UseCollabhostAuthorization();

// Endpoints
app.MapRegistryEndpoints();
app.MapProxyEndpoints();
app.MapDashboardEndpoints();
app.MapFilesystemEndpoints();
app.MapSystemEndpoints();

// SPA fallback
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

// Health
app.MapDefaultEndpoints();

await app.RunAsync();
