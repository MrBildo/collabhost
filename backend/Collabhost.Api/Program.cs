using Collabhost.Api.Data;
using Collabhost.Api.Registry;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Aspire service defaults
builder.AddServiceDefaults();

// Database
builder.Services.AddDataAccess(builder.Configuration);

// Memory cache
builder.Services.AddMemoryCache();

// Subsystems
builder.Services.AddRegistry();

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

    app.MapOpenApi();
    app.UseCors(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
}

// SPA fallback
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

// Health
app.MapDefaultEndpoints();

await app.RunAsync();
