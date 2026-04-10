using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Dashboard;
using Collabhost.Api.Data;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Events;
using Collabhost.Api.Filesystem;
using Collabhost.Api.Mcp;
using Collabhost.Api.Platform;
using Collabhost.Api.Probes;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;
using Collabhost.Api.Supervisor;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Aspire service defaults
builder.AddServiceDefaults();

// JSON: accept string enum values (e.g. "administrator", "agent") in request bodies
builder.Services.ConfigureHttpJsonOptions
(
    options => options.SerializerOptions.Converters.Add
    (
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
    )
);

// Database
builder.Services.AddDataAccess(builder.Configuration);

// Memory cache
builder.Services.AddMemoryCache();

// Auth
using var earlyLoggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
var earlyLogger = earlyLoggerFactory.CreateLogger("Startup");

builder.Services.AddCollabhostAuthorization(builder.Configuration, earlyLogger);

// Type store
builder.Services.AddTypeStore();

// Subsystems
builder.Services.AddActivityLog();
builder.Services.AddRegistry();
builder.Services.AddCapabilities();
builder.Services.AddEventBus();
builder.Services.AddSupervisor();
builder.Services.AddProxy(builder.Configuration);
builder.Services.AddProbes();

// MCP
builder.Services.AddMcp();

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

    // TypeStore startup gate -- load and validate built-in types before hosted services
    var typeStore = app.Services.GetRequiredService<TypeStore>();
    await typeStore.LoadAsync(CancellationToken.None);

    var proxySeeder = scope.ServiceProvider.GetRequiredService<ProxyAppSeeder>();
    await proxySeeder.SeedAsync(CancellationToken.None);

    app.MapOpenApi();
    app.UseCors(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
}

// Middleware
app.UseCollabhostAuthorization();

// Endpoints
app.MapActivityLogEndpoints();
app.MapUserEndpoints();
app.MapRegistryEndpoints();
app.MapProxyEndpoints();
app.MapDashboardEndpoints();
app.MapFilesystemEndpoints();
app.MapSystemEndpoints();
app.MapMcpEndpoints();

// SPA fallback
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

// Health
app.MapDefaultEndpoints();

await app.RunAsync();
