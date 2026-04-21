using System.Globalization;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Registry;

using ActivityEvent = Collabhost.Api.ActivityLog.ActivityEvent;

namespace Collabhost.Api.Proxy;

public class ProxyAppSeeder
(
    AppStore appStore,
    TypeStore typeStore,
    ProxySettings settings,
    ActivityEventStore activityEventStore,
    ILogger<ProxyAppSeeder> logger
)
{
    private readonly AppStore _appStore = appStore
        ?? throw new ArgumentNullException(nameof(appStore));

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

        // CaddyResolver is the single source of truth for binary resolution (§6.4.1).
        // Precedence: COLLABHOST_CADDY_PATH env > Proxy:BinaryPath config > bundled sidecar.
        // Returns null when no Caddy can be found -- proxy subsystem soft-fails.
        var resolvedPath = CaddyResolver.Resolve(_settings, _logger);

        if (resolvedPath is null)
        {
            _logger.LogWarning
            (
                "No Caddy binary found -- proxy subsystem disabled. " +
                "Resolution order: COLLABHOST_CADDY_PATH env var, Proxy:BinaryPath setting, " +
                "then bundled sidecar at '{BundledPath}'. " +
                "Install Caddy (https://caddyserver.com/docs/install) or set COLLABHOST_CADDY_PATH. " +
                "proxyState will report 'disabled' on /api/v1/status until resolved.",
                Path.Combine
                (
                    AppContext.BaseDirectory,
                    OperatingSystem.IsWindows() ? "caddy.exe" : "caddy"
                )
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

        await _appStore.CreateAsync(proxyApp, cancellationToken);

        await CreateCapabilityOverridesAsync(proxyApp.Id, resolvedPath, cancellationToken);

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

    private async Task CreateCapabilityOverridesAsync
    (
        Ulid appId,
        string resolvedPath,
        CancellationToken cancellationToken
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

        await _appStore.SaveOverrideAsync(appId, "process", processOverride, cancellationToken);

        // Auto-start capability override: always start with Collabhost
        var autoStartOverride = JsonSerializer.Serialize
        (
            new { enabled = true },
            _jsonOptions
        );

        await _appStore.SaveOverrideAsync(appId, "auto-start", autoStartOverride, cancellationToken);

        // Artifact capability override: location is the directory containing the binary
        var artifactOverride = JsonSerializer.Serialize
        (
            new { location = Path.GetDirectoryName(resolvedPath) },
            _jsonOptions
        );

        await _appStore.SaveOverrideAsync(appId, "artifact", artifactOverride, cancellationToken);
    }
}
