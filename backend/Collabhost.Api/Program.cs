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

// Resolve TypeStore settings once so preflight and DI registration use the same values
// without reading the env var twice (LOW-3).
var typeStoreSettings = TypeStoreRegistration.ResolveSettings(builder.Configuration);
var effectiveUserTypesDir = TypeStoreRegistration.ResolveEffectiveUserTypesDirectory(typeStoreSettings);

using (var preflightLoggerFactory = LoggerFactory.Create(logging => logging.AddConsole()))
{
    var preflightLogger = preflightLoggerFactory.CreateLogger("StartupPreflight");
    var preflightResult = StartupPreflight.Validate(effectiveDataDir, effectiveUserTypesDir, preflightLogger);

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
builder.Services.AddTypeStore(typeStoreSettings);

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

// Startup phase 6 -- TypeStore.LoadAsync (§8). Runs unconditionally in every environment.
// Built-in validation failure is a packaging bug (exit 30); user-type validation failure is an
// operator configuration error (exit 31). Separate exit codes let bundled smoke tests distinguish
// "our package broke" from "operator configured wrong."
var typeStore = app.Services.GetRequiredService<TypeStore>();

try
{
    await typeStore.LoadAsync(CancellationToken.None);
}
catch (TypeStoreValidationException ex)
{
    if (ex.IsBuiltIn)
    {
        app.Logger.LogCritical
        (
            ex,
            "Startup halted: built-in type validation failed ({ErrorCount} errors) -- packaging bug",
            ex.Errors.Count
        );

        StartupStderr.Write
        (
            summary: "built-in type validation failed -- this is a packaging bug",
            details: BuildTypeStoreErrorDetails(ex.Errors),
            recoverySteps:
            [
                "File an issue at https://github.com/MrBildo/collabhost/issues and include this stderr block.",
                "Re-install the previous Collabhost version from releases."
            ],
            exitCode: 30
        );

        return 30;
    }

    app.Logger.LogCritical
    (
        ex,
        "Startup halted: user-type validation failed ({ErrorCount} errors) in {Directory}",
        ex.Errors.Count,
        effectiveUserTypesDir
    );

    StartupStderr.Write
    (
        summary: $"invalid user type JSON in '{effectiveUserTypesDir}'",
        details: BuildTypeStoreErrorDetails(ex.Errors),
        recoverySteps:
        [
            $"Inspect the files listed above in {effectiveUserTypesDir}.",
            "Fix or remove the invalid file(s) and restart Collabhost."
        ],
        exitCode: 31
    );

    return 31;
}

// Startup phase 7 -- ProxyAppSeeder (§9). Idempotent: a first boot seeds, subsequent boots no-op.
// Unexpected throws (DB write failure, transaction rollback, etc.) halt with exit 40.
await using (var seederScope = app.Services.CreateAsyncScope())
{
    var proxySeeder = seederScope.ServiceProvider.GetRequiredService<ProxyAppSeeder>();

    try
    {
        await proxySeeder.SeedAsync(CancellationToken.None);
    }
    catch (Exception ex)
    {
        app.Logger.LogCritical(ex, "Startup halted: proxy app seeder threw unexpectedly");

        StartupStderr.Write
        (
            summary: "proxy app seeding failed",
            details: [("Exception", $"{ex.GetType().Name}: {ex.Message}")],
            recoverySteps:
            [
                "This usually indicates database corruption or a transaction rollback.",
                "File an issue at https://github.com/MrBildo/collabhost/issues and include this stderr block.",
                "If the issue persists, restore the most recent pre-migration backup from data/backups/."
            ],
            exitCode: 40
        );

        return 40;
    }
}

// Dev-only surface (OpenAPI + CORS) stays gated. These are developer conveniences, not
// production behavior.
if (app.Environment.IsDevelopment())
{
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

// Startup phase 9 -- TypeStore.StartWatching (§8.3). FileSystemWatcher for hot-reload of user
// types. Placed after middleware wiring so no FSW event can fire before the pipeline is ready.
// Stays enabled in Production; ReloadAsync handles runtime errors non-fatally (keep current
// snapshot + warn). LoadAsync is the boot-critical path; StartWatching is operational.
typeStore.StartWatching();

await app.RunAsync();

return 0;

// Helper for composing TypeStore validation errors into the StartupStderr detail shape.
static IReadOnlyList<(string Label, string Value)> BuildTypeStoreErrorDetails
(
    IReadOnlyList<TypeStoreValidationError> errors
)
{
    var details = new List<(string Label, string Value)>(errors.Count);

    foreach (var error in errors)
    {
        details.Add(($"{error.Source}: {error.FieldPath}", error.Message));
    }

    return details;
}
