using System.ComponentModel;
using System.Security;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;
using Collabhost.Api.Supervisor;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Collabhost.Api.Mcp;

#pragma warning disable MA0076 // Ulid.ToString is not locale-sensitive
#pragma warning disable MA0011 // Ulid.ToString/TryParse is not locale-sensitive
[McpServerToolType]
public class RegistrationTools
(
    AppStore appStore,
    TypeStore typeStore,
    ProcessSupervisor supervisor,
    ProxyManager proxy,
    ICurrentUser currentUser,
    ActivityEventStore activityEventStore,
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

    private readonly ICurrentUser _currentUser = currentUser
        ?? throw new ArgumentNullException(nameof(currentUser));

    private readonly ActivityEventStore _activityEventStore = activityEventStore
        ?? throw new ArgumentNullException(nameof(activityEventStore));

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
    [Description("Registers a new application on the platform. Requires an app type slug, a display name, and an install directory. The app is created in 'stopped' status and must be started separately with start_app. Workflow: call list_app_types to discover valid types and their registration schemas. Optionally call browse_filesystem to find the install directory and detect_strategy to check what Collabhost can auto-detect.")]
    public async Task<CallToolResult> RegisterAppAsync
    (
        [Description("Display name for the application (e.g., 'My API Server').")] string name,
        [Description("App type slug from list_app_types (e.g., 'dotnet-app', 'nodejs-app', 'static-site', 'executable', 'system-service').")] string appTypeSlug,
        [Description("Absolute path to the application's directory on the host filesystem.")] string installDirectory,
        [Description("Optional JSON object with additional registration settings specific to the app type. Example: {\"process\":{\"workingDirectory\":\"/app\",\"executablePath\":\"./myapp\"}}")] string? settings,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return McpResponseFormatter.InvalidParameters("name is required.");
        }

        if (string.IsNullOrWhiteSpace(appTypeSlug))
        {
            return McpResponseFormatter.InvalidParameters("appTypeSlug is required.");
        }

        if (string.IsNullOrWhiteSpace(installDirectory))
        {
            return McpResponseFormatter.InvalidParameters("installDirectory is required.");
        }

        var appType = _typeStore.GetBySlug(appTypeSlug);

        if (appType is null)
        {
            return McpResponseFormatter.AppTypeNotFound(appTypeSlug);
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
                    if (!settingsObject.ContainsKey("process"))
                    {
                        settingsObject["process"] = new JsonObject();
                    }

                    var processSection = settingsObject["process"]!.AsObject();

                    processSection["workingDirectory"] ??= JsonValue.Create(installDirectory);
                }

                // Apply installDirectory into the artifact capability if it has one
                var hasArtifact = _typeStore.HasBinding(appType.Slug, "artifact");

                if (hasArtifact)
                {
                    if (!settingsObject.ContainsKey("artifact"))
                    {
                        settingsObject["artifact"] = new JsonObject();
                    }

                    var artifactSection = settingsObject["artifact"]!.AsObject();

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
                        sectionKey, sectionChanges, isNewApp: true
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

        // Phase 1b coexistence: resolve the ULID for the FK (removed in Phase 2)
        var appTypeId = await _appStore.GetAppTypeIdBySlugAsync(appType.Slug, ct);

        if (appTypeId is null)
        {
            return McpResponseFormatter.InvalidParameters("App type not found in database.");
        }

        // All validation passed -- now create the app and persist overrides
        var app = new App
        {
            Slug = derivedSlug,
            DisplayName = name.Trim(),
            AppTypeId = appTypeId.Value,
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
        var hasRouting = _typeStore.HasBinding(appType.Slug, "routing");
        var hasProcessCapability = _typeStore.HasBinding(appType.Slug, "process");

        if (hasRouting && !hasProcessCapability)
        {
            _proxy.DisableRoute(app.Slug);
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
                new { slug = app.Slug, id = app.Id.ToString(), status = "stopped" }
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
        CancellationToken ct
    )
    {
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

        _supervisor.CleanupDeletedApp(app.Id);

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
    public static CallToolResult BrowseFilesystem
    (
        [Description("Absolute filesystem path to list. If omitted, returns root drives/directories.")] string? path
    )
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return BrowseRoots();
        }

        if (!IsValidPath(path))
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
    [Description("Analyzes a directory to determine what Collabhost can auto-detect for a given app type. For dotnet-app, checks for .runtimeconfig.json (published output) or .csproj (source project). For nodejs-app, checks for package.json with a start script. Returns the detected strategy name and evidence files found. Use this after choosing an app type and install directory, before calling register_app, to understand what configuration Collabhost will auto-discover vs. what must be specified manually.")]
    public static CallToolResult DetectStrategy
    (
        [Description("Absolute filesystem path to analyze.")] string path,
        [Description("App type slug (e.g., 'dotnet-app', 'nodejs-app'). Use list_app_types to see valid values.")] string appTypeSlug
    )
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return McpResponseFormatter.InvalidParameters("path is required.");
        }

        if (string.IsNullOrWhiteSpace(appTypeSlug))
        {
            return McpResponseFormatter.InvalidParameters("appTypeSlug is required.");
        }

        if (!IsValidPath(path))
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

        var (strategy, evidence) = DetectStrategyForAppType(fullPath, appTypeSlug);

        var result = new
        {
            strategy,
            evidenceFiles = evidence
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

    private static (string Strategy, List<string> Evidence) DetectStrategyForAppType
    (
        string directory,
        string appTypeSlug
    )
    {
        if (string.Equals(appTypeSlug, "dotnet-app", StringComparison.Ordinal))
        {
            var runtimeConfigs = Directory.GetFiles(directory, "*.runtimeconfig.json");

            if (runtimeConfigs.Length > 0)
            {
                return
                (
                    FormatStrategyName(DiscoveryStrategy.DotNetRuntimeConfiguration),
                    [.. runtimeConfigs.Select(f => Path.GetFileName(f))]
                );
            }

            var projects = Directory.GetFiles(directory, "*.csproj");

            if (projects.Length > 0)
            {
                return
                (
                    FormatStrategyName(DiscoveryStrategy.DotNetProject),
                    [.. projects.Select(f => Path.GetFileName(f))]
                );
            }

            return (FormatStrategyName(DiscoveryStrategy.Manual), []);
        }

        if (string.Equals(appTypeSlug, "nodejs-app", StringComparison.Ordinal))
        {
            var packageJsonPath = Path.Combine(directory, "package.json");

            if (File.Exists(packageJsonPath))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(packageJsonPath));

                    if (document.RootElement.TryGetProperty("scripts", out var scripts)
                        && scripts.TryGetProperty("start", out _))
                    {
                        return (FormatStrategyName(DiscoveryStrategy.PackageJson), ["package.json"]);
                    }
                }
                catch (JsonException)
                {
                    // Malformed package.json -- fall through to Manual
                }

                return (FormatStrategyName(DiscoveryStrategy.Manual), ["package.json"]);
            }

            return (FormatStrategyName(DiscoveryStrategy.Manual), []);
        }

        return (FormatStrategyName(DiscoveryStrategy.Manual), []);
    }

    private static string FormatStrategyName(DiscoveryStrategy strategy)
    {
        var name = strategy.ToString();

        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static bool IsValidPath(string path)
    {
        var invalidChars = Path.GetInvalidPathChars();

        return !path.AsSpan().ContainsAny(invalidChars);
    }
}
#pragma warning restore MA0011
#pragma warning restore MA0076
