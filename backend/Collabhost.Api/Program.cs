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

if (args.Any(a => a is "--version" or "-v"))
{
    Console.WriteLine($"Collabhost {VersionInfo.Current}");
    return 0;
}

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// WebApplication.CreateBuilder adds an env-var provider at builder-construction time, but it
// sits below any sources added later. The .Local.json load above now shadows env vars in dev.
// Re-adding here pushes env vars back to the top of the provider chain per §2.5 precedence
// (env > appsettings.json > default). Dev-only effect -- .Local.json does not ship to production.
builder.Configuration.AddEnvironmentVariables();

// Aspire service defaults
builder.AddServiceDefaults();

// Startup phase 2 -- Environment preflight (§5). Runs before DI is built and before any hosted
// service wires itself up. A halt here means we never touch the DB.
var (_, resolvedDataDir) = DataRegistration.ResolveConnectionString(builder.Configuration);
var effectiveDataDir = resolvedDataDir ?? Path.Combine(AppContext.BaseDirectory, "data");

using (var preflightLoggerFactory = LoggerFactory.Create(logging => logging.AddConsole()))
{
    var preflightLogger = preflightLoggerFactory.CreateLogger("StartupPreflight");
    var preflightResult = StartupPreflight.Validate(effectiveDataDir, preflightLogger);

    if (!preflightResult.Success)
    {
        preflightLogger.LogCritical
        (
            "Startup preflight failed: {Summary}",
            preflightResult.FailureSummary
        );

        StartupStderr.Write
        (
            summary: preflightResult.FailureSummary!,
            details: preflightResult.FailureDetails,
            recoverySteps: preflightResult.RecoverySteps,
            exitCode: 10
        );

        return 10;
    }
}

// JSON: accept string enum values (e.g. "administrator", "agent") in request bodies
builder.Services.ConfigureHttpJsonOptions
(
    options => options.SerializerOptions.Converters.Add
    (
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
    )
);

// Platform (BootVersionWriter seam -- #156.1 PR #95 MED-2)
builder.Services.AddPlatform();

// Database
builder.Services.AddDataAccess(builder.Configuration);

// Memory cache
builder.Services.AddMemoryCache();

// Auth
using var earlyLoggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
var earlyLogger = earlyLoggerFactory.CreateLogger("Startup");

builder.Services.AddCollabhostAuthorization(builder.Configuration, earlyLogger);

// Type store
builder.Services.AddTypeStore(builder.Configuration);

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

// Startup phase 4+5 -- Pre-migration backup + schema migration (§6, §7). Runs unconditionally
// in every environment, including Production. On failure, halt before any hosted service starts.
var toSemver = VersionInfo.Current;
var fromSemver = BootVersionTracker.Read(effectiveDataDir, app.Logger);
var backupsDir = Path.Combine(effectiveDataDir, StartupPreflight.BackupsSubdirectory);
var migrationRunner = app.Services.GetRequiredService<MigrationRunner>();

try
{
    var migrationOutcome = await migrationRunner.MigrateWithBackupAsync
    (
        effectiveDataDir,
        backupsDir,
        fromSemver,
        toSemver,
        CancellationToken.None
    );

    if (migrationOutcome.Migrated)
    {
        app.Logger.LogInformation
        (
            "Schema migrated from {FromSemver} to {ToSemver}. Backup: {BackupPath}. Applied: {Count}",
            fromSemver,
            toSemver,
            migrationOutcome.BackupPath ?? "(first-run, no backup)",
            migrationOutcome.AppliedMigrations.Count
        );
    }
}
catch (MigrationFailedException ex)
{
    app.Logger.LogCritical
    (
        ex,
        "Startup halted during schema migration: {Summary}. Backup={BackupPath} Migration={MigrationAttempted}",
        ex.Summary,
        ex.BackupPath ?? "(none)",
        ex.MigrationAttempted ?? "(pre-flight)"
    );

    var details = new List<(string Label, string Value)>();

    if (ex.BackupPath is not null)
    {
        details.Add(("Backup created at", ex.BackupPath));
    }

    if (ex.MigrationAttempted is not null)
    {
        details.Add(("Migration attempted", ex.MigrationAttempted));
    }

    if (ex.InnerException is not null)
    {
        details.Add(("Exception", $"{ex.InnerException.GetType().Name}: {ex.InnerException.Message}"));
    }

    StartupStderr.Write
    (
        summary: ex.Summary,
        details: details,
        recoverySteps: ex.RecoverySteps,
        exitCode: ex.ExitCode
    );

    return ex.ExitCode;
}

if (app.Environment.IsDevelopment())
{
    // TypeStore + ProxyAppSeeder gates remain in #156.1. Lifted in #156.2.
    await using var scope = app.Services.CreateAsyncScope();

    var typeStore = app.Services.GetRequiredService<TypeStore>();
    await typeStore.LoadAsync(CancellationToken.None);
    typeStore.StartWatching();

    var proxySeeder = scope.ServiceProvider.GetRequiredService<ProxyAppSeeder>();
    await proxySeeder.SeedAsync(CancellationToken.None);

    app.MapOpenApi();
    app.UseCors(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
}

// Write the last-boot-version sentinel once the host is fully started (§6.2.1). Routed through
// IBootVersionWriter so integration-test fixtures can substitute a no-op writer and keep temp
// data directories free of the sentinel side-effect (PR #95 MED-2).
var bootVersionWriter = app.Services.GetRequiredService<IBootVersionWriter>();

app.Lifetime.ApplicationStarted.Register
(
    () => bootVersionWriter.Write(effectiveDataDir, toSemver)
);

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

return 0;
