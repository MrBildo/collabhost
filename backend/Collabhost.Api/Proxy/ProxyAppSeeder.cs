using System.Globalization;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Data;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Registry;

using ActivityEvent = Collabhost.Api.ActivityLog.ActivityEvent;

namespace Collabhost.Api.Proxy;

public class ProxyAppSeeder
(
    AppStore appStore,
    IDbContextFactory<AppDbContext> dbFactory,
    TypeStore typeStore,
    ProxySettings settings,
    ActivityEventStore activityEventStore,
    ILogger<ProxyAppSeeder> logger
)
{
    private readonly AppStore _appStore = appStore
        ?? throw new ArgumentNullException(nameof(appStore));

    private readonly IDbContextFactory<AppDbContext> _dbFactory = dbFactory
        ?? throw new ArgumentNullException(nameof(dbFactory));

    private readonly TypeStore _typeStore = typeStore
        ?? throw new ArgumentNullException(nameof(typeStore));

    private readonly ProxySettings _settings = settings
        ?? throw new ArgumentNullException(nameof(settings));

    private readonly ActivityEventStore _activityEventStore = activityEventStore
        ?? throw new ArgumentNullException(nameof(activityEventStore));

    private readonly ILogger<ProxyAppSeeder> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var existingProxy = await _appStore.GetBySlugAsync("proxy", cancellationToken);

        if (existingProxy is not null)
        {
            _logger.LogInformation("Proxy app already registered -- skipping seed");

            return;
        }

        // CaddyResolver is the single source of truth for binary resolution.
        // Precedence: COLLABHOST_CADDY_PATH env > Proxy:BinaryPath config > null.
        // Returns null when no Caddy is configured -- proxy subsystem soft-fails.
        var resolvedPath = CaddyResolver.Resolve(_settings, _logger);

        if (resolvedPath is null)
        {
            _logger.LogWarning
            (
                "No Caddy binary configured -- proxy subsystem disabled. " +
                "Resolution order: COLLABHOST_CADDY_PATH env var, then Proxy:BinaryPath in appsettings.json. " +
                "Re-run the installer to seed the bundled-sidecar path, or set COLLABHOST_CADDY_PATH " +
                "to an absolute path. proxyState will report 'disabled' on /api/v1/status until resolved."
            );

            return;
        }

        var appType = _typeStore.GetBySlug("system-service");

        if (appType is null)
        {
            _logger.LogWarning("No 'system-service' app type found -- cannot seed proxy app");

            return;
        }

        var proxyApp = new App
        {
            Slug = "proxy",
            DisplayName = "Proxy",
            AppTypeSlug = "system-service"
        };

        // All seed writes (App row + CapabilityOverride rows) happen inside a single
        // transaction so a first-boot SIGINT cannot leave a partial state -- the DB
        // is either fully seeded or untouched after a crash or cancellation.
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        db.Apps.Add(proxyApp);

        BuildCapabilityOverrides(proxyApp.Id, resolvedPath, db);

        await db.SaveChangesAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);

        // Invalidate AppStore cache entries that were bypassed by the direct DB write above.
        _appStore.Invalidate("proxy");
        _appStore.InvalidateOverrides(proxyApp.Id);

        try
        {
            await _activityEventStore.RecordAsync
            (
                new ActivityEvent
                {
                    EventType = ActivityEventTypes.AppSeeded,
                    ActorId = ActivityActor.SystemId,
                    ActorName = ActivityActor.SystemName,
                    AppId = proxyApp.Id.ToString(null, CultureInfo.InvariantCulture),
                    AppSlug = proxyApp.Slug
                },
                CancellationToken.None
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record activity event for proxy app seed");
        }

        _logger.LogInformation("Proxy app seeded -- binary at '{BinaryPath}'", resolvedPath);
    }

    private static void BuildCapabilityOverrides
    (
        Ulid appId,
        string resolvedPath,
        AppDbContext db
    )
    {
        // Process capability override: manual discovery, caddy binary as command.
        // Only the "run" subcommand is seeded here -- the ProxyArgumentProvider injects
        // the bootstrap config (--config) at process start time with the session-scoped
        // admin port. Do NOT add --config or --admin here; they are session-scoped.
        var processOverride = JsonSerializer.Serialize
        (
            new
            {
                discoveryStrategy = "Manual",
                command = resolvedPath,
                arguments = "run",
                workingDirectory = Path.GetDirectoryName(resolvedPath),
                shutdownTimeoutSeconds = 10
            },
            _jsonOptions
        );

        db.CapabilityOverrides.Add(new CapabilityOverride
        {
            AppId = appId,
            CapabilitySlug = "process",
            ConfigurationJson = processOverride
        });

        // Auto-start capability override: always start with Collabhost
        var autoStartOverride = JsonSerializer.Serialize
        (
            new { enabled = true },
            _jsonOptions
        );

        db.CapabilityOverrides.Add(new CapabilityOverride
        {
            AppId = appId,
            CapabilitySlug = "auto-start",
            ConfigurationJson = autoStartOverride
        });

        // Artifact capability override: location is the directory containing the binary
        var artifactOverride = JsonSerializer.Serialize
        (
            new { location = Path.GetDirectoryName(resolvedPath) },
            _jsonOptions
        );

        db.CapabilityOverrides.Add(new CapabilityOverride
        {
            AppId = appId,
            CapabilitySlug = "artifact",
            ConfigurationJson = artifactOverride
        });
    }
}
