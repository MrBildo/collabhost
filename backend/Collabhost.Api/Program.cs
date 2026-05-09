using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Dashboard;
using Collabhost.Api.Data;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Events;
using Collabhost.Api.Filesystem;
using Collabhost.Api.Installation;
using Collabhost.Api.Mcp;
using Collabhost.Api.Platform;
using Collabhost.Api.Portal;
using Collabhost.Api.Probes;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;
using Collabhost.Api.Supervisor;

using Microsoft.AspNetCore.Hosting.Server;

if (args.Any(a => a is "--version" or "-v"))
{
    Console.WriteLine($"Collabhost {VersionInfo.Current}");
    return 0;
}

// Installer-invoked subcommand: merge a freshly-shipped appsettings.json against the operator's
// on-disk file (preserve edits, refresh untouched defaults, add new keys). Runs before any host
// setup so this path never touches the database, the supervisor, or the network. Card #161.
if (args.Length > 0 && args[0] == "--merge-appsettings")
{
    return AppSettingsMergeCli.Run(args[1..], Console.Out, Console.Error);
}

// Anchor the host's content root explicitly. Card #164 baked in the rule that we never
// trust the operator's CWD. Card #246 (c2-A) extends that rule: when ASPNETCORE_CONTENTROOT
// is set (system-install layout via the systemd unit), honor it; otherwise fall through to
// AppContext.BaseDirectory (Windows install.ps1, Linux user-scope, Aspire dev).
//
// The resolved value is passed to WebApplicationOptions explicitly so the CWD-immunity from
// #164 holds in BOTH the env-var-set and env-var-unset paths -- WebApplication.CreateBuilder
// would otherwise treat an unset env var as "fall through to CWD," which is exactly what
// #164 forbids.
var contentRoot = Environment.GetEnvironmentVariable("ASPNETCORE_CONTENTROOT")
    ?? AppContext.BaseDirectory;

var builder = WebApplication.CreateBuilder
(
    new WebApplicationOptions
    {
        Args = args,
        ContentRootPath = contentRoot
    }
);

// Card #246 (c2-A): operator-editable config file path. When set (system-install layout),
// the binary loads /etc/collabhost/appsettings.json directly. When unset, only the framework's
// default ContentRoot/appsettings.json applies (already loaded by WebApplication.CreateBuilder
// above). The explicit AddJsonFile is added LATER in the provider chain so it wins on key
// conflicts when both files happen to exist -- which is the operator's intent when they set
// the env var.
var configPath = Environment.GetEnvironmentVariable("COLLABHOST_CONFIG_PATH");
if (!string.IsNullOrWhiteSpace(configPath))
{
    builder.Configuration.AddJsonFile(configPath, optional: true, reloadOnChange: true);
}

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// WebApplication.CreateBuilder adds an env-var provider at builder-construction time, but it
// sits below any sources added later. The .Local.json load above now shadows env vars in dev.
// Re-adding here pushes env vars back to the top of the provider chain
// (env > appsettings.json > default). Dev-only effect -- .Local.json does not ship to production.
builder.Configuration.AddEnvironmentVariables();

// Bind Kestrel to Hosting:ListenAddress + Hosting:ListenPort for the installed-binary path.
// In dev, launchSettings.json (applicationUrl) and under Aspire, ASPNETCORE_URLS set the
// "urls" configuration key before this point -- those win. In production there is no
// launchSettings.json and no ASPNETCORE_URLS, so Kestrel would otherwise fall through to its
// default :5000 while Caddy dials localhost:{Hosting:ListenPort} and every reverse_proxy hop
// returns 502 (#176). Use ListenAddress + ListenPort as the fallback dial/listen target so
// Caddy and Kestrel cannot disagree on port.
//
// ListenAddress defaults to "localhost" so the canonical posture is unchanged: edge TLS
// terminates at Caddy on :443 and the API stays loopback-only. Headless-server installs
// that reach the API directly (no DNS for *.collab.internal, no CA trust for the bundled
// internal CA) can set ListenAddress=0.0.0.0 to expose every interface, or pin it to a
// specific NIC IP. Card #218.
//
// No added log line here: Microsoft.Hosting.Lifetime already writes "Now listening on: <url>"
// at Info, which the operator sees on stdout in production and which the KestrelListenPort
// integration tests already parse. An additional Console.WriteLine earlier landed on xunit's
// per-test StringWriter capture and blew up subsequent tests once the writer disposed (#176
// CI fallout). The framework log covers the observability need without touching Console
// directly.
var configuredUrls = builder.Configuration["urls"]
    ?? builder.Configuration["ASPNETCORE_URLS"];

if (string.IsNullOrWhiteSpace(configuredUrls))
{
    var resolvedHosting = HostingRegistration.ResolveSettings(builder.Configuration);

    var selfUrl = HostingUrlBuilder.Build(resolvedHosting.ListenAddress, resolvedHosting.ListenPort);

    builder.WebHost.UseUrls(selfUrl);
}

// Aspire service defaults
builder.AddServiceDefaults();

// Startup phase 2 -- Environment preflight. Runs before DI is built and before any hosted
// service wires itself up. A halt here means we never touch the DB.
var (_, resolvedDataDir) = DataRegistration.ResolveConnectionString(builder.Configuration);
var effectiveDataDir = resolvedDataDir ?? Path.Combine(AppContext.BaseDirectory, "data");

// Crash log directory + retention. Resolved before any preflight so a preflight failure
// can still write a crash file. Default is {DataDir}/logs/. Card #162.
var crashLogDir = CrashLog.ResolveDirectory(builder.Configuration, effectiveDataDir);
var crashLogRetention = CrashLog.ResolveRetention(builder.Configuration);

// Last-chance unhandled-exception hook. Fires when the runtime is about to terminate
// without graceful shutdown -- past the point where the host's logging pipeline can be
// counted on to flush. Writing a crash file is best-effort; if we throw here we'd mask
// the original failure, so any IO error inside CrashLog.TryWrite is swallowed by design.
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    if (e.ExceptionObject is not Exception ex)
    {
        return;
    }

    var content = CrashLog.BuildContent
    (
        DateTimeOffset.UtcNow,
        $"unhandled exception ({ex.GetType().Name})",
        [("Message", ex.Message)],
        [
            "Inspect the crash log for the full stack trace.",
            "File an issue at https://github.com/MrBildo/collabhost/issues and attach the crash log."
        ],
        -1,
        ex
    );

    CrashLog.TryWrite(crashLogDir, DateTimeOffset.UtcNow, content, crashLogRetention);
};

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

        StartupStderr.WriteAndPersist
        (
            preflightResult.FailureSummary!,
            preflightResult.FailureDetails,
            preflightResult.RecoverySteps,
            10,
            crashLogDir,
            crashLogRetention
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

// Hosting settings -- ListenPort lives on its own section because it's a hosting concern,
// not a proxy concern. The proxy reads it to know what to dial.
builder.Services.AddHosting(builder.Configuration);

// Database
builder.Services.AddDataAccess(builder.Configuration);

// Memory cache
builder.Services.AddMemoryCache();

// Auth
builder.Services.AddCollabhostAuthorization(builder.Configuration);

// Type store
builder.Services.AddTypeStore(typeStoreSettings);

// Subsystems
builder.Services.AddActivityLog();
builder.Services.AddRegistry();
builder.Services.AddCapabilities();
builder.Services.AddEventBus();
builder.Services.AddSupervisor();
builder.Services.AddProxy(builder.Configuration);
builder.Services.AddPortal(builder.Configuration);
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

// Startup phase 4+5 -- Pre-migration backup + schema migration. Runs unconditionally
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

    StartupStderr.WriteAndPersist
    (
        ex.Summary,
        details,
        ex.RecoverySteps,
        ex.ExitCode,
        crashLogDir,
        crashLogRetention,
        ex
    );

    return ex.ExitCode;
}

// Startup phase 6 -- TypeStore.LoadAsync. Runs unconditionally in every environment.
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

        StartupStderr.WriteAndPersist
        (
            "built-in type validation failed -- this is a packaging bug",
            BuildTypeStoreErrorDetails(ex.Errors),
            [
                "File an issue at https://github.com/MrBildo/collabhost/issues and include this stderr block.",
                "Re-install the previous Collabhost version from releases."
            ],
            30,
            crashLogDir,
            crashLogRetention,
            ex
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

    StartupStderr.WriteAndPersist
    (
        $"invalid user type JSON in '{effectiveUserTypesDir}'",
        BuildTypeStoreErrorDetails(ex.Errors),
        [
            $"Inspect the files listed above in {effectiveUserTypesDir}.",
            "Fix or remove the invalid file(s) and restart Collabhost."
        ],
        31,
        crashLogDir,
        crashLogRetention,
        ex
    );

    return 31;
}

// Startup phase 7 -- ProxyAppSeeder. Idempotent: a first boot seeds, subsequent boots no-op.
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

        StartupStderr.WriteAndPersist
        (
            "proxy app seeding failed",
            [("Exception", $"{ex.GetType().Name}: {ex.Message}")],
            [
                "This usually indicates database corruption or a transaction rollback.",
                "File an issue at https://github.com/MrBildo/collabhost/issues and include this stderr block.",
                "If the issue persists, restore the most recent pre-migration backup from data/backups/."
            ],
            40,
            crashLogDir,
            crashLogRetention,
            ex
        );

        return 40;
    }
}

// Startup phase 8 -- UserSeedService. Implements the admin-key 3-scenario model:
// (1) blind first run generates + prints, (2) configured first run seeds silently,
// (3) subsequent boot with a new configured key inserts an additional Admin user
// (break-glass additive). Unexpected throws halt with exit 40 -- same code as
// ProxyAppSeeder; both are "a seeder threw during startup" and share a recovery shape.
await using (var userSeedScope = app.Services.CreateAsyncScope())
{
    var userSeeder = userSeedScope.ServiceProvider.GetRequiredService<UserSeedService>();

    try
    {
        await userSeeder.SeedAsync(CancellationToken.None);
    }
    catch (Exception ex)
    {
        app.Logger.LogCritical(ex, "Startup halted: admin user seeder threw unexpectedly");

        StartupStderr.WriteAndPersist
        (
            "admin user seeding failed",
            [("Exception", $"{ex.GetType().Name}: {ex.Message}")],
            [
                "This usually indicates database corruption or a transaction rollback.",
                "File an issue at https://github.com/MrBildo/collabhost/issues and include this stderr block.",
                "If the issue persists, restore the most recent pre-migration backup from data/backups/."
            ],
            40,
            crashLogDir,
            crashLogRetention,
            ex
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

// Portal: static-asset shipping for the embedded React dashboard. UseDefaultFiles +
// UseStaticFiles + a SPA-fallback middleware all run in the middleware phase BEFORE auth
// so the dashboard shell is reachable unauthenticated; auth is enforced at API-call time
// by <AuthGate>. The auth wall continues to gate /api/v1/* via UseCollabhostAuthorization
// below. Card #184.
app.UsePortal();

// Write the last-boot-version sentinel once the host is fully started. Routed through
// IBootVersionWriter so integration-test fixtures can substitute a no-op writer and keep temp
// data directories free of the sentinel side-effect (PR #95 MED-2).
var bootVersionWriter = app.Services.GetRequiredService<IBootVersionWriter>();

app.Lifetime.ApplicationStarted.Register
(
    () => bootVersionWriter.Write(effectiveDataDir, toSemver)
);

// Cross-validate Hosting:ListenPort against Kestrel's actual listen port once the host has
// bound (#165). IServerAddressesFeature is only populated after Kestrel binds, so this
// runs from ApplicationStarted rather than earlier in startup. Soft warning only -- a
// mismatch is an operator misconfiguration, not a halt condition. TestServer (used by
// WebApplicationFactory<Program>) does not expose listen addresses, so the validator
// short-circuits to "skipped" in integration tests.
app.Lifetime.ApplicationStarted.Register
(
    () =>
    {
        var server = app.Services.GetRequiredService<IServer>();
        var hostingSettings = app.Services.GetRequiredService<HostingSettings>();

        var listeningAddresses = ListenPortValidator.GetListeningAddresses(server);
        var outcome = ListenPortValidator.Validate(hostingSettings.ListenPort, listeningAddresses);

        if (outcome.Status == ListenPortValidationStatus.Mismatch)
        {
            // Pre-rendered message keeps the operator-facing copy in one place
            // (ListenPortValidator) and lets the structured logger emit it as a single
            // message field. The configured/observed values are also surfaced as
            // structured fields for downstream log queries.
            app.Logger.LogWarning
            (
                "Hosting:ListenPort mismatch detected. {Message} (configured={ConfiguredListenPort} observed={ObservedPorts})",
                outcome.RenderedMessage,
                outcome.ConfiguredListenPort,
                string.Join(',', outcome.ObservedPorts)
            );
        }
    }
);

// Portal reachability preflight. Soft validator -- warns, never halts. Two legitimate
// "missing" states exist (packaging regression where wwwroot/ didn't ship, or an operator
// intentionally stripping the dashboard); halting boot trades a degraded mode (API still
// serves; dashboard 404s) for a fully unreachable host. Card #184.
app.Lifetime.ApplicationStarted.Register
(
    () =>
    {
        var outcome = PortalReachabilityCheck.Validate(app.Environment.ContentRootPath, app.Logger);

        if (outcome.Status != PortalReachabilityStatus.Ok)
        {
            app.Logger.LogWarning
            (
                "Portal reachability check: {Status} at {WwwrootPath}. {RecoverySteps}",
                outcome.Status,
                outcome.WwwrootPath,
                string.Join(' ', outcome.RecoverySteps)
            );
        }
    }
);

// Emit a log line on graceful shutdown so operators can confirm the signal was received.
// Without this, a process going silent after SIGINT/SIGTERM is indistinguishable from
// the signal not having been delivered -- both cases look identical in a truncated log
// tail. ApplicationStopping fires once the host acknowledges the stop request.
// ApplicationStopped fires after all hosted services have returned. See card #189.
app.Lifetime.ApplicationStopping.Register
(
    () => app.Logger.LogInformation("Collabhost shutting down...")
);

app.Lifetime.ApplicationStopped.Register
(
    () => app.Logger.LogInformation("Collabhost shutdown complete.")
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

// Health
app.MapDefaultEndpoints();

// Startup phase 9 -- TypeStore.StartWatching. FileSystemWatcher for hot-reload of user types.
// Placed after middleware wiring so no FSW event can fire before the pipeline is ready.
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
