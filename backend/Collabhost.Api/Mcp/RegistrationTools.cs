using System.ComponentModel;
using System.Security;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Probes;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;
using Collabhost.Api.Shared;
using Collabhost.Api.Supervisor;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Collabhost.Api.Mcp;

#pragma warning disable MA0076 // Ulid.ToString is not locale-sensitive
#pragma warning disable MA0011 // Ulid.ToString/TryParse is not locale-sensitive
// Card #332: every tool takes an optional `authKey` per-call argument. Resolution happens
// at the top of each body via McpRequestAuthenticator.
[McpServerToolType]
public class RegistrationTools
(
    AppStore appStore,
    TypeStore typeStore,
    ProcessSupervisor supervisor,
    ProxyManager proxy,
    ExternalTargetSettings externalTargetSettings,
    ICurrentUser currentUser,
    ActivityEventStore activityEventStore,
    AppDataPathResolver dataPathResolver,
    McpRequestAuthenticator authenticator,
    ILogger<RegistrationTools> logger
)
{
    private readonly AppStore _appStore = appStore
        ?? throw new ArgumentNullException(nameof(appStore));

    private readonly TypeStore _typeStore = typeStore
        ?? throw new ArgumentNullException(nameof(typeStore));

    private readonly ProcessSupervisor _supervisor = supervisor
        ?? throw new ArgumentNullException(nameof(supervisor));

    private readonly ProxyManager _proxy = proxy
        ?? throw new ArgumentNullException(nameof(proxy));

    // Card #348, D3. Carried through to CapabilityResolver.ValidateEdits when
    // a registration settings JSON carries an external-target section.
    private readonly ExternalTargetSettings _externalTargetSettings = externalTargetSettings
        ?? throw new ArgumentNullException(nameof(externalTargetSettings));

    private readonly ICurrentUser _currentUser = currentUser
        ?? throw new ArgumentNullException(nameof(currentUser));

    private readonly ActivityEventStore _activityEventStore = activityEventStore
        ?? throw new ArgumentNullException(nameof(activityEventStore));

    private readonly AppDataPathResolver _dataPathResolver = dataPathResolver
        ?? throw new ArgumentNullException(nameof(dataPathResolver));

    private readonly McpRequestAuthenticator _authenticator = authenticator
        ?? throw new ArgumentNullException(nameof(authenticator));

    private readonly ILogger<RegistrationTools> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    [McpServerTool
    (
        Name = "register_app",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false
    )]
    [Description("Registers a new application on the platform. Requires an app type slug, a display name, and an install directory. The app is created in 'stopped' status and must be started separately with start_app. The response includes 'writableDataPath' -- a platform-provisioned absolute directory inside Collabhost's writable data root; configure any app that writes to disk (e.g. a SQLite connection string) to write there rather than into its install directory, which may be read-only under a hardened deployment. Workflow: call list_app_types to discover valid types and their registration schemas. Optionally call browse_filesystem to find the install directory and detect_strategy to check what Collabhost can auto-detect.")]
    public async Task<CallToolResult> RegisterAppAsync
    (
        [Description("Display name for the application (e.g., 'My API Server').")] string name,
        [Description("App type slug from list_app_types (e.g., 'dotnet-app', 'nodejs-app', 'static-site', 'executable', 'system-service', 'external-route').")] string appTypeSlug,
        // installDirectory loosened to conditionally-required per Card #348 D4.
        // For app types that bind process or artifact (dotnet-app, nodejs-app, static-site,
        // executable, system-service) the directory is still required and validated below
        // after type lookup. For routing-only types whose upstream is operator-declared
        // (external-route via external-target binding) the parameter is meaningless and
        // accepted as empty or null.
        [Description("Absolute path to the application's directory on the host filesystem. Required for app types that bind process or artifact (dotnet-app, nodejs-app, static-site, executable, system-service). Accepted as empty or null for routing-only types like external-route, which have no install directory.")] string? installDirectory = null,
        // Explicit `= null` default is load-bearing: the MCP tool-binding marshaller treats params with no
        // C# default as required. Card #331.
        [Description("Optional JSON object with additional registration settings specific to the app type. Examples: dotnet-app -> {\"process\":{\"command\":\"./myapp\",\"arguments\":\"--port 5000\",\"discoveryStrategy\":\"Manual\"}}; external-route -> {\"external-target\":{\"host\":\"localhost\",\"port\":11235,\"scheme\":\"http\"}}. Valid process keys: command, arguments, workingDirectory, discoveryStrategy, shutdownTimeoutSeconds, startupGracePeriodSeconds, maxStartupRetries. Valid external-target keys: host, port, scheme.")] string? settings = null,
        [Description(McpAuthDescriptions.AuthKey)] string? authKey = null,
        CancellationToken ct = default
    )
    {
        var authError = await _authenticator.AuthenticateAsync(authKey, "register_app", ct);

        if (authError is not null)
        {
            return authError;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return McpResponseFormatter.InvalidParameters("name is required.");
        }

        if (string.IsNullOrWhiteSpace(appTypeSlug))
        {
            return McpResponseFormatter.InvalidParameters("appTypeSlug is required.");
        }

        var appType = _typeStore.GetBySlug(appTypeSlug);

        if (appType is null)
        {
            return McpResponseFormatter.AppTypeNotFound(appTypeSlug);
        }

        // installDirectory is required only for app types that bind `process`
        // (uses it as workingDirectory) or `artifact` (uses it as location).
        // For sparse-capability routing-only types like external-route, the
        // parameter is meaningless and accepted as null or empty. Card #348, D4.
        var directoryRequired =
            _typeStore.HasBinding(appType.Slug, "process")
            || _typeStore.HasBinding(appType.Slug, "artifact");

        if (directoryRequired && string.IsNullOrWhiteSpace(installDirectory))
        {
            return McpResponseFormatter.InvalidParameters
            (
                $"installDirectory is required for app type '{appType.Slug}' "
                + "(this type uses an install directory for its process or artifact location)."
            );
        }

        // Derive slug from name
        var derivedSlug = name.Trim().ToLowerInvariant()
            .Replace(' ', '-');

        var (isValid, slugError) = Slug.Validate(derivedSlug);

        if (!isValid)
        {
            return McpResponseFormatter.InvalidParameters
            (
                $"Cannot derive a valid slug from name '{name}': {slugError} "
                + "Provide a name that produces a lowercase alphanumeric slug (hyphens allowed)."
            );
        }

        var exists = await _appStore.ExistsBySlugAsync(derivedSlug, ct);

        if (exists)
        {
            return McpResponseFormatter.InvalidParameters
            (
                $"An app with slug '{derivedSlug}' already exists. Use list_apps to see existing apps."
            );
        }

        // Validate all settings BEFORE creating the app to ensure registration is transactional
        var validatedOverrides = new List<(string SectionKey, JsonObject Overrides)>();

        if (!string.IsNullOrWhiteSpace(settings))
        {
            JsonObject? settingsObject;

            try
            {
                settingsObject = JsonNode.Parse(settings)?.AsObject();
            }
            catch (JsonException ex)
            {
                return McpResponseFormatter.InvalidParameters
                (
                    $"Invalid JSON in settings parameter: {ex.Message}"
                );
            }

            if (settingsObject is not null)
            {
                // Apply installDirectory into the process capability if it has one
                var hasProcess = _typeStore.HasBinding(appType.Slug, "process");

                if (hasProcess)
                {
                    var processSection = settingsObject.EnsureSection("process");

                    processSection["workingDirectory"] ??= JsonValue.Create(installDirectory);
                }

                // Apply installDirectory into the artifact capability if it has one
                var hasArtifact = _typeStore.HasBinding(appType.Slug, "artifact");

                if (hasArtifact)
                {
                    var artifactSection = settingsObject.EnsureSection("artifact");

                    artifactSection["location"] ??= JsonValue.Create(installDirectory);
                }

                foreach (var (sectionKey, sectionValueNode) in settingsObject)
                {
                    if (sectionValueNode is not JsonObject sectionChanges)
                    {
                        continue;
                    }

                    var validationErrors = CapabilityResolver.ValidateEdits
                    (
                        sectionKey, sectionChanges, true, _externalTargetSettings.AllowPublicHosts
                    );

                    if (validationErrors.Count > 0)
                    {
                        return McpResponseFormatter.InvalidParameters
                        (
                            $"Validation errors for '{sectionKey}': {string.Join("; ", validationErrors)}"
                        );
                    }

                    validatedOverrides.Add((sectionKey, sectionChanges));
                }
            }
        }
        else
        {
            // No explicit settings -- inject installDirectory into capabilities if available
            var hasProcess = _typeStore.HasBinding(appType.Slug, "process");

            if (hasProcess)
            {
                var processOverride = new JsonObject
                {
                    ["workingDirectory"] = JsonValue.Create(installDirectory)
                };

                validatedOverrides.Add(("process", processOverride));
            }

            var hasArtifact = _typeStore.HasBinding(appType.Slug, "artifact");

            if (hasArtifact)
            {
                var artifactOverride = new JsonObject
                {
                    ["location"] = JsonValue.Create(installDirectory)
                };

                validatedOverrides.Add(("artifact", artifactOverride));
            }
        }

        // All validation passed -- now create the app and persist overrides
        var app = new App
        {
            Slug = derivedSlug,
            DisplayName = name.Trim(),
            AppTypeSlug = appType.Slug
        };

        await _appStore.CreateAsync(app, ct);

        foreach (var (sectionKey, overrideObject) in validatedOverrides)
        {
            await _appStore.SaveOverrideAsync
            (
                app.Id,
                sectionKey,
                overrideObject.ToJsonString(McpResponseFormatter.JsonOptions),
                ct
            );
        }

        // Routing-only apps (e.g. static sites) start with their route disabled
        // because the operator still needs to populate an artifact directory
        // before the route is meaningful. External-route apps (Card #348, D8)
        // are the inversion: there is no "build artifact" intermediate step,
        // the upstream is the operator-declared host:port, so the honest
        // default is enabled-at-registration. If the operator later wants to
        // disable, stop_app is the lever.
        //
        // EnableRoute failure mode. If the channel is full or Caddy is down,
        // the call returns but the route stays disabled until the next sync.
        // The operator gets a 200 from register_app and can call start_app
        // to retry. Same posture as today's process apps -- creation
        // succeeds, start can fail later.
        var hasRouting = _typeStore.HasBinding(appType.Slug, "routing");
        var hasProcessCapability = _typeStore.HasBinding(appType.Slug, "process");
        var hasExternalTarget = _typeStore.HasBinding(appType.Slug, "external-target");

        if (hasRouting && !hasProcessCapability)
        {
            if (hasExternalTarget)
            {
                _proxy.EnableRoute(app.Slug);
                _proxy.RequestSync();
            }
            else
            {
                _proxy.DisableRoute(app.Slug);
            }
        }

        try
        {
            await _activityEventStore.RecordAsync
            (
                new ActivityEvent
                {
                    EventType = ActivityEventTypes.AppCreated,
                    ActorId = _currentUser.UserId.ToString(),
                    ActorName = _currentUser.User.Name,
                    AppId = app.Id.ToString(),
                    AppSlug = app.Slug,
                    MetadataJson = JsonSerializer.Serialize
                    (
                        new { appTypeSlug = appType.Slug, displayName = app.DisplayName }
                    )
                },
                ct
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record activity event for app.created (slug={Slug})", app.Slug);
        }

        return McpResponseFormatter.Success
        (
            McpResponseFormatter.ToJson
            (
                new
                {
                    slug = app.Slug,
                    id = app.Id.ToString(),
                    status = "stopped",
                    writableDataPath = _dataPathResolver.ResolveFor(app.Slug)
                }
            )
        );
    }

    [McpServerTool
    (
        Name = "delete_app",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false
    )]
    [Description("Stops and permanently deletes a registered application. The app's process is stopped first (10-second graceful timeout), then the app is removed from the registry. All settings and capability overrides are deleted. This action cannot be undone. Use get_app to verify the app before deleting.")]
    public async Task<CallToolResult> DeleteAppAsync
    (
        [Description("The app's unique slug identifier. Use list_apps to find available slugs.")] string slug,
        [Description(McpAuthDescriptions.AuthKey)] string? authKey = null,
        CancellationToken ct = default
    )
    {
        var authError = await _authenticator.AuthenticateAsync(authKey, "delete_app", ct);

        if (authError is not null)
        {
            return authError;
        }

        // delete_app is admin-only -- see Entitlements._agentTools. The authenticator above
        // already enforces this via Entitlements.CanAccessTool, but checking IsAdministrator
        // here is a belt-and-suspenders double-check at the destructive call site (Card #332,
        // matches the spirit of the pre-#332 session-time tool-list filter that removed
        // delete_app from the visible surface for non-admin callers).
        if (!_currentUser.IsAdministrator)
        {
            return McpResponseFormatter.InvalidParameters
            (
                "delete_app requires an administrator authKey."
            );
        }

        var app = await _appStore.GetBySlugAsync(slug, ct);

        if (app is null)
        {
            return McpResponseFormatter.AppNotFound(slug);
        }

        // Capture before delete -- app won't exist after _appStore.DeleteAppAsync
        var appId = app.Id.ToString();
        var appSlug = app.Slug;
        var appDisplayName = app.DisplayName;

        // Stop if running (10s graceful timeout, force-kill fallback)
        var process = _supervisor.GetProcess(app.Id);

        if (process is not null && process.IsRunning)
        {
            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                await _supervisor.StopAppAsync(app.Id, timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Timeout expired -- force kill
                try
                {
                    await _supervisor.KillAppAsync(app.Id, CancellationToken.None);
                }
                catch (InvalidOperationException)
                {
                    // Already stopped
                }
            }
            catch (InvalidOperationException)
            {
                // Already stopped
            }
        }

        await _appStore.DeleteAppAsync(app.Id, ct);

        _supervisor.CleanupDeletedApp(app.Id, appSlug);

        try
        {
            await _activityEventStore.RecordAsync
            (
                new ActivityEvent
                {
                    EventType = ActivityEventTypes.AppDeleted,
                    ActorId = _currentUser.UserId.ToString(),
                    ActorName = _currentUser.User.Name,
                    AppId = appId,
                    AppSlug = appSlug,
                    MetadataJson = JsonSerializer.Serialize(new { displayName = appDisplayName })
                },
                ct
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record activity event for app.deleted (slug={Slug})", appSlug);
        }

        return McpResponseFormatter.Success
        (
            $"Deleted app '{appSlug}' ({appDisplayName}). This action cannot be undone."
        );
    }

    [McpServerTool
    (
        Name = "browse_filesystem",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Lists directories at a given path on the host machine. When path is omitted, returns the root drives (Windows) or '/' (Unix). Use this during app registration to find the install directory interactively. Returns child directory names and paths. Hidden and system directories are excluded.")]
    public async Task<CallToolResult> BrowseFilesystemAsync
    (
        // Explicit `= null` default is load-bearing: the MCP tool-binding marshaller treats params with no
        // C# default as required. Card #331.
        [Description("Absolute filesystem path to list. If omitted, returns root drives/directories.")] string? path = null,
        [Description(McpAuthDescriptions.AuthKey)] string? authKey = null,
        CancellationToken ct = default
    )
    {
        var authError = await _authenticator.AuthenticateAsync(authKey, "browse_filesystem", ct);

        if (authError is not null)
        {
            return authError;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return BrowseRoots();
        }

        if (!path.IsValidPath())
        {
            return McpResponseFormatter.InvalidParameters
            (
                "The specified path contains invalid characters."
            );
        }

        string fullPath;

        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return McpResponseFormatter.InvalidParameters($"Invalid path: {ex.Message}");
        }

        if (!Directory.Exists(fullPath))
        {
            return McpResponseFormatter.InvalidParameters
            (
                $"Directory '{fullPath}' does not exist."
            );
        }

        IReadOnlyList<object> directories;

        try
        {
            directories = EnumerateChildDirectories(fullPath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            return McpResponseFormatter.InvalidParameters($"Access denied to '{fullPath}'.");
        }

        var parentPath = Directory.GetParent(fullPath)?.FullName;

        var result = new
        {
            currentPath = fullPath,
            parentPath,
            directories
        };

        return McpResponseFormatter.Success(McpResponseFormatter.ToJson(result));
    }

    [McpServerTool
    (
        Name = "detect_strategy",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Analyzes a directory and reports evidence relevant to the chosen app type -- runtime configs, project files, single-file binaries, package manifests, static-site entry points, or executables. Returns a strategy hint plus the list of evidence files found. The 'notApplicable' strategy is returned for app types that don't go through process discovery (static-site, executable directories with no clear binary). Use this after choosing an app type and install directory, before calling register_app, to understand what configuration Collabhost will auto-discover vs. what must be specified manually.")]
    public async Task<CallToolResult> DetectStrategyAsync
    (
        [Description("Absolute filesystem path to analyze.")] string path,
        [Description("App type slug (e.g., 'dotnet-app', 'nodejs-app', 'static-site', 'executable'). Use list_app_types to see valid values.")] string appTypeSlug,
        [Description(McpAuthDescriptions.AuthKey)] string? authKey = null,
        CancellationToken ct = default
    )
    {
        var authError = await _authenticator.AuthenticateAsync(authKey, "detect_strategy", ct);

        if (authError is not null)
        {
            return authError;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return McpResponseFormatter.InvalidParameters("path is required.");
        }

        if (string.IsNullOrWhiteSpace(appTypeSlug))
        {
            return McpResponseFormatter.InvalidParameters("appTypeSlug is required.");
        }

        if (!path.IsValidPath())
        {
            return McpResponseFormatter.InvalidParameters
            (
                "The specified path contains invalid characters."
            );
        }

        string fullPath;

        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return McpResponseFormatter.InvalidParameters($"Invalid path: {ex.Message}");
        }

        if (!Directory.Exists(fullPath))
        {
            return McpResponseFormatter.InvalidParameters
            (
                $"Directory '{fullPath}' does not exist."
            );
        }

        var evidence = ArtifactEvidenceCollector.Collect(fullPath, appTypeSlug);

        var paths = new List<string>(evidence.Signals.Count);

        foreach (var signal in evidence.Signals)
        {
            paths.Add(signal.Path);
        }

        var result = new
        {
            strategy = evidence.SuggestedStrategy,
            evidenceFiles = paths
        };

        return McpResponseFormatter.Success(McpResponseFormatter.ToJson(result));
    }

    private static CallToolResult BrowseRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            IReadOnlyList<object> drives =
            [
                .. DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                        .Select
                        (
                            d => (object)new
                            {
                                name = d.Name.TrimEnd('\\'),
                                path = d.RootDirectory.FullName
                            }
                        )
            ];

            var rootResult = new
            {
                currentPath = string.Empty,
                parentPath = (string?)null,
                directories = drives
            };

            return McpResponseFormatter.Success(McpResponseFormatter.ToJson(rootResult));
        }

        // Unix: single root
        var unixDirectories = EnumerateChildDirectories("/");

        var unixResult = new
        {
            currentPath = "/",
            parentPath = (string?)null,
            directories = unixDirectories
        };

        return McpResponseFormatter.Success(McpResponseFormatter.ToJson(unixResult));
    }

    private static IReadOnlyList<object> EnumerateChildDirectories(string directoryPath)
    {
        var directory = new DirectoryInfo(directoryPath);

        return
        [
            .. directory
                .EnumerateDirectories()
                .Where
                (
                    d => !d.Attributes.HasFlag(FileAttributes.Hidden)
                        && !d.Attributes.HasFlag(FileAttributes.System)
                )
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(d => (object)new { name = d.Name, path = d.FullName })
        ];
    }
}
#pragma warning restore MA0011
#pragma warning restore MA0076

file static class RegistrationToolExtensions
{
    extension(JsonObject parent)
    {
        public JsonObject EnsureSection(string key)
        {
            if (parent[key] is JsonObject existing)
            {
                return existing;
            }

            var section = new JsonObject();
            parent[key] = section;

            return section;
        }
    }
}
