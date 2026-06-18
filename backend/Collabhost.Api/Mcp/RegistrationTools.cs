using System.ComponentModel;
using System.Security;

using Collabhost.Api.Authorization;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Operations;
using Collabhost.Api.Probes;
using Collabhost.Api.Registry;
using Collabhost.Api.Shared;

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
    CreateAppOperation createAppOperation,
    DeleteAppOperation deleteAppOperation,
    ICurrentUser currentUser,
    AppDataPathResolver dataPathResolver,
    McpRequestAuthenticator authenticator
)
{
    // Both write tools are migrated to the operation spine (code-structure-conventions §8).
    // register_app's full create sequence lives in CreateAppOperation, and delete_app's full
    // stop-then-delete sequence (including the probe-cache invalidation REST always had and MCP did
    // not) lives in DeleteAppOperation. The deps those two bodies held that nothing else in this tool
    // needs left the ctor with them -- ProcessSupervisor, ProxyManager, ActivityEventStore, and the
    // ILogger that delete_app was the last consumer of, plus register_app's earlier ProxySettings and
    // ExternalTargetSettings. What remains is exactly the surface's own concerns. AppStore backs
    // delete_app's MCP-surface AppNotFound pre-check, the shape REST does not return. TypeStore backs
    // register_app's MCP-surface directoryRequired gate. ICurrentUser backs delete_app's admin-only
    // double-check at the destructive call site. AppDataPathResolver backs register_app's
    // writableDataPath. Then the two operations and the authenticator.
    private readonly AppStore _appStore = appStore
        ?? throw new ArgumentNullException(nameof(appStore));

    private readonly TypeStore _typeStore = typeStore
        ?? throw new ArgumentNullException(nameof(typeStore));

    // The migrated registration operation injected directly (code-structure-conventions §8: no
    // dispatcher). register_app adapts its raw input into the command, calls this, and maps the result.
    private readonly CreateAppOperation _createAppOperation = createAppOperation
        ?? throw new ArgumentNullException(nameof(createAppOperation));

    // The migrated delete operation injected directly (code-structure-conventions §8: no dispatcher).
    // delete_app adapts the slug into the command, calls this, and maps the result.
    private readonly DeleteAppOperation _deleteAppOperation = deleteAppOperation
        ?? throw new ArgumentNullException(nameof(deleteAppOperation));

    private readonly ICurrentUser _currentUser = currentUser
        ?? throw new ArgumentNullException(nameof(currentUser));

    private readonly AppDataPathResolver _dataPathResolver = dataPathResolver
        ?? throw new ArgumentNullException(nameof(dataPathResolver));

    private readonly McpRequestAuthenticator _authenticator = authenticator
        ?? throw new ArgumentNullException(nameof(authenticator));

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
        [Description("App type slug from list_app_types (e.g., 'dotnet-app', 'nodejs-app', 'static-site', 'executable', 'system-service', 'external-route', 'internal-service').")] string appTypeSlug,
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

        // MCP-surface type lookup for the directoryRequired gate (Marcus R4). The operation looks the
        // type up again (shared core); this is a cached TypeStore.GetBySlug dictionary read, and the
        // LifecycleTools adapters already double-look-up, so the second read is established + cheap. The
        // gate itself is single-surface (REST has no installDirectory parameter, Card #348 D4) so it
        // stays at the surface with its MCP-specific prose -- and the type-not-found here keeps the MCP
        // AppTypeNotFound shape (the operation's NotFound also maps to AppTypeNotFound via the result
        // mapping below, R5, so the shape is identical whether the surface or the operation catches it).
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

        // Derive slug from name (the MCP-surface transform: REST takes its slug as-given). Both the
        // derive transform and its derive-failure error prose are single-surface concerns that stay
        // here (Marcus §1.5); the command carries the already-valid, final-persisted derivedSlug.
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

        // Assemble the raw input into the normalized command's Overrides JsonObject (MCP-specific
        // section assembly -- the installDirectory injection). Validation + exists-check + create now
        // live in the operation; this adapter only produces the divergent input shape.
        var (overrides, parseError) = AssembleOverrides(appType, installDirectory, settings);

        // Deliberate, disclosed ordering -- Card #406 PR 6, finding F-1. The settings-JSON parse runs
        // HERE at the adapter, BEFORE the operation's exists-check. Pre-migration the MCP exists-check
        // ran first, so a doubly-invalid request -- an existing slug AND malformed settings JSON --
        // returned the exists conflict, where it now surfaces the parse error first. This reorder is
        // FORCED by the spine and is not a regression: the parse builds the command the operation
        // consumes, so it cannot run after the operation. Do NOT preserve the old order by adding a
        // surface-level exists-check here -- that would duplicate the operation's exists-check and
        // re-leak core logic into the adapter, the exact anti-pattern this migration removed. Zero
        // state-impact -- both inputs were always errors, so only WHICH error surfaces first changed,
        // and only on the MCP surface.
        if (parseError is not null)
        {
            return McpResponseFormatter.InvalidParameters(parseError);
        }

        var command = new CreateAppCommand(derivedSlug, name.Trim(), appTypeSlug, overrides);

        var result = await _createAppOperation.ExecuteAsync(command, ct);

        if (!result.IsSuccess)
        {
            return result.ToCallToolResult(appTypeSlug);
        }

        var outcome = result.Value!;

        return McpResponseFormatter.Success
        (
            McpResponseFormatter.ToJson
            (
                new
                {
                    slug = outcome.Slug,
                    id = outcome.Id.ToString(),
                    status = "stopped",
                    writableDataPath = _dataPathResolver.ResolveFor(outcome.Slug),
                    helpfulNextSteps = outcome.Hints
                }
            )
        );
    }

    // The MCP-specific section assembly: parse the raw `settings` JSON string into a JsonObject and
    // inject installDirectory into process.workingDirectory / artifact.location (gated on the type's
    // bindings). Returns the normalized Overrides plus an optional parse-error message the caller maps
    // to the MCP InvalidParameters shape (kept at the surface -- the JSON-string format is an
    // MCP-input concern, REST has no equivalent). Byte-for-byte preserves the pre-migration assembly:
    // a valid-but-non-object settings JSON yields no overrides and no injection (the pre-migration code
    // skipped the whole block when JsonNode.Parse(...)?.AsObject() was null), exactly as here.
    private (JsonObject Overrides, string? ParseError) AssembleOverrides
    (
        AppType appType,
        string? installDirectory,
        string? settings
    )
    {
        if (!string.IsNullOrWhiteSpace(settings))
        {
            JsonObject? settingsObject;

            try
            {
                settingsObject = JsonNode.Parse(settings)?.AsObject();
            }
            catch (JsonException ex)
            {
                return ([], $"Invalid JSON in settings parameter: {ex.Message}");
            }

            if (settingsObject is null)
            {
                return ([], null);
            }

            // Apply installDirectory into the process capability if it has one
            if (_typeStore.HasBinding(appType.Slug, "process"))
            {
                var processSection = settingsObject.EnsureSection("process");

                processSection["workingDirectory"] ??= JsonValue.Create(installDirectory);
            }

            // Apply installDirectory into the artifact capability if it has one
            if (_typeStore.HasBinding(appType.Slug, "artifact"))
            {
                var artifactSection = settingsObject.EnsureSection("artifact");

                artifactSection["location"] ??= JsonValue.Create(installDirectory);
            }

            return (settingsObject, null);
        }

        // No explicit settings -- inject installDirectory into capabilities if available
        var overrides = new JsonObject();

        if (_typeStore.HasBinding(appType.Slug, "process"))
        {
            overrides["process"] = new JsonObject
            {
                ["workingDirectory"] = JsonValue.Create(installDirectory)
            };
        }

        if (_typeStore.HasBinding(appType.Slug, "artifact"))
        {
            overrides["artifact"] = new JsonObject
            {
                ["location"] = JsonValue.Create(installDirectory)
            };
        }

        return (overrides, null);
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

        // The not-found pre-check is an MCP-surface concern kept above the operation call: MCP
        // returns its own AppNotFound shape (a specific "use list_apps" message), where REST returns
        // an empty 404 -- a genuine surface divergence that stays at the surface, exactly as the
        // lifecycle adapters do. The operation re-loads inside the same request scope and runs the
        // stop-then-delete sequence; this adapter only adapts the slug into the command and maps the
        // result back to the MCP shape.
        var app = await _appStore.GetBySlugAsync(slug, ct);

        if (app is null)
        {
            return McpResponseFormatter.AppNotFound(slug);
        }

        var result = await _deleteAppOperation.ExecuteAsync(new DeleteAppCommand(slug), ct);

        return result.ToCallToolResult();
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
    [Description("Analyzes a directory and reports evidence relevant to a chosen app type -- runtime configs, project files, single-file binaries, package manifests, static-site entry points, or executables. When appTypeSlug is provided, returns a single { strategy, evidenceFiles } for that type. When omitted, returns { perType: { <slug>: { strategy, evidenceFiles } } } with one entry per app type the analyzer has rules for (dotnet-app, nodejs-app, static-site, executable) -- useful when you want to discover what's in a directory before committing to an app type. The 'notApplicable' strategy is returned for app types that don't go through process discovery (static-site, executable directories with no clear binary). Use this before calling register_app to understand what configuration Collabhost will auto-discover vs. what must be specified manually.")]
    public async Task<CallToolResult> DetectStrategyAsync
    (
        [Description("Absolute filesystem path to analyze.")] string path,
        [Description("App type slug (e.g., 'dotnet-app', 'nodejs-app', 'static-site', 'executable'). Use list_app_types to see valid values. Optional -- omit to receive a per-type map covering every analyzed app type.")] string? appTypeSlug = null,
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

        var payload = string.IsNullOrWhiteSpace(appTypeSlug)
            ? CollectPerType(fullPath)
            : CollectSingle(fullPath, appTypeSlug);

        return McpResponseFormatter.Success(McpResponseFormatter.ToJson(payload));
    }

    private static object CollectSingle(string fullPath, string appTypeSlug)
    {
        var evidence = ArtifactEvidenceCollector.Collect(fullPath, appTypeSlug);

        return new
        {
            strategy = evidence.SuggestedStrategy,
            evidenceFiles = ToPaths(evidence)
        };
    }

    private static object CollectPerType(string fullPath)
    {
        var perType = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (var slug in ArtifactEvidenceCollector.KnownAppTypeSlugs)
        {
            var evidence = ArtifactEvidenceCollector.Collect(fullPath, slug);

            perType[slug] = new
            {
                strategy = evidence.SuggestedStrategy,
                evidenceFiles = ToPaths(evidence)
            };
        }

        return new { perType };
    }

    private static List<string> ToPaths(ArtifactEvidence evidence)
    {
        var paths = new List<string>(evidence.Signals.Count);

        foreach (var signal in evidence.Signals)
        {
            paths.Add(signal.Path);
        }

        return paths;
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

// File-scoped mapping from the surface-agnostic CreateAppOutcome result back to the MCP result shape
// (§7: the surface holds only its file-scoped mapping). K-1 (Kai's PR-1 forward note):
// OperationResult.FailureKind defaults to ordinal-0 NotFound on a success, so this is the FAILURE half
// only -- the caller gates on IsSuccess first and shapes success inline (the { slug, id, status,
// writableDataPath, helpfulNextSteps } object).
//
// Unlike the lifecycle all-to-InvalidParameters collapse, register_app's mapper needs a distinct
// NotFound arm (Marcus R5): the operation's type-not-found returns OperationResult.NotFound, and MCP's
// pre-migration shape for that was the rich-prose AppTypeNotFound(slug) -- a different, slug-bearing
// shape than the bare InvalidParameters. The bare OperationResult.Error string does not carry the slug
// structurally, so the mapper takes the in-scope appTypeSlug (still the tool parameter) and rebuilds
// AppTypeNotFound from it. Conflict (exists-check) and Validation (section errors) both map to
// InvalidParameters, the single MCP error shape for those -- byte-identical to the pre-migration MCP
// path EXCEPT the exists-check message: the operation returns the bare "An app with slug '...' already
// exists." (the REST message), dropping the pre-migration MCP "Use list_apps to see existing apps."
// suffix. Disclosed as a zero-information-loss prose normalization (Marcus R2, the same PR-5 shape Kai
// passed). The Validation section-errors message is byte-identical: the pre-migration MCP path wrapped
// it in "Validation errors for '{section}': " but ValidateEdits already prefixes each error with the
// section + field, so dropping the redundant wrapper is the same zero-loss normalization PR 5 made for
// update_settings (Marcus R2 family).
file static class CreateAppOperationResultMapping
{
    public static CallToolResult ToCallToolResult
    (
        this OperationResult<CreateAppOutcome> result,
        string appTypeSlug
    ) =>
        result.FailureKind switch
        {
            OperationFailureKind.NotFound => McpResponseFormatter.AppTypeNotFound(appTypeSlug),
            _ => McpResponseFormatter.InvalidParameters(result.Error ?? string.Empty),
        };
}

// File-scoped mapping from the surface-agnostic delete outcome back to the MCP result shape (§7: the
// surface holds only its file-scoped mapping). K-1 (Kai's PR-1 forward note):
// OperationResult.FailureKind defaults to ordinal-0 NotFound on a success, so the success arm is gated
// on IsSuccess FIRST -- FailureKind is only read on the failure path. The success shape is the exact
// "Deleted app '{slug}' ({displayName}). This action cannot be undone." message the pre-migration body
// returned. The MCP-specific not-found (AppNotFound) is handled by the pre-load in the tool body above
// this mapping, so NotFound cannot reach here on the normal path; any failure that does reach here maps
// to InvalidParameters, the single MCP error shape (the operation never produces Validation/Conflict on
// the delete path -- surfacing the operation's message verbatim is the right defensive shape).
file static class DeleteAppOperationResultMapping
{
    public static CallToolResult ToCallToolResult(this OperationResult<DeleteAppOutcome> result)
    {
        if (result.IsSuccess)
        {
            var outcome = result.Value!;

            return McpResponseFormatter.Success
            (
                $"Deleted app '{outcome.Slug}' ({outcome.DisplayName}). This action cannot be undone."
            );
        }

        return McpResponseFormatter.InvalidParameters(result.Error ?? string.Empty);
    }
}
