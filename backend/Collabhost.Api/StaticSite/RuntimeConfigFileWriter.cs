using Collabhost.Api.Capabilities;
using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Registry;

namespace Collabhost.Api.StaticSite;

// Writes the operator-managed runtime-config file for a static-site app to disk.
// Card #336.
//
// Trigger points (both load-bearing -- see Marcus's S55 #5 design review,
// preconditions 3 + 4):
//
//   1. Route-enable in StartAppAsync (routing-only branch). Writer runs BEFORE
//      proxy.EnableRoute so Caddy never serves a stale value for an arbitrary
//      window. Writer failure halts the route enable.
//   2. Settings-save when the route is currently enabled and the changes touch
//      the runtime-config-file capability. Write failure surfaces in the save
//      response as a partial-success (override persisted, file on disk did not
//      update -- operator-actionable).
//
// Empty-Values invariant: the writer short-circuits when `resolved.Values.Count
// == 0`. Three migration states (no override row / override row without `values`
// key / override row with `values: {}`) all converge to this branch through the
// CapabilityResolver. Asserted in tests; load-bearing for CLAUDE.md Rule 3
// (operator-maintained config.json must not be silently overwritten).
//
// Delete-after-write semantics: when the operator transitions from non-empty
// Values to empty Values, the writer no-ops on the next trigger and the file on
// disk from the prior write stays as-is. The platform does not silently delete
// operator-visible artifacts; if the operator wants the file gone they delete
// it themselves. Bill ruling, S55 #6.
//
// Atomic write: render to a temp file in the same directory then File.Move with
// overwrite. Cross-platform-safe-enough for v1 -- atomic on Linux (rename(2)),
// best-effort-atomic on Windows (MoveFileEx + MOVEFILE_REPLACE_EXISTING).
//
// Composes with #308's `/config.json::Cache-Control: no-cache` default on the
// static-site routing capability: every value change is visible at the next
// portal fetch with no Caddy reload required. Without that header, A would
// need a Caddy reload on every write; with it, the writer alone suffices.
public class RuntimeConfigFileWriter
(
    CapabilityStore capabilityStore,
    ILogger<RuntimeConfigFileWriter> logger
)
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly CapabilityStore _capabilityStore = capabilityStore
        ?? throw new ArgumentNullException(nameof(capabilityStore));

    private readonly ILogger<RuntimeConfigFileWriter> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    // Renders the runtime-config file for the given app if the resolved Values
    // are non-empty. No-op when Values is empty (migration safety -- see class
    // docstring). Throws RuntimeConfigFileWriteException on any failure when
    // Values is non-empty so the caller (route-enable, settings-save) can
    // surface an operator-actionable error.
    public async Task RenderAsync(App app, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(app);

        var config = await _capabilityStore.ResolveAsync<RuntimeConfigFileConfiguration>
        (
            "runtime-config-file", app, ct
        );

        // Capability not declared on this app's type -- nothing to write.
        if (config is null)
        {
            return;
        }

        // EMPTY-VALUES INVARIANT (writer's first action against resolved values).
        // Load-bearing for CLAUDE.md Rule 3: if Values is empty the writer makes
        // ZERO filesystem calls, preserving any operator-maintained file on disk.
        // Asserted by RuntimeConfigFileWriter_EmptyValues_DoesNotTouchDisk.
        if (config.Values.Count == 0)
        {
            return;
        }

        var artifactConfig = await _capabilityStore.ResolveAsync<ArtifactConfiguration>
        (
            "artifact", app, ct
        );

        if (artifactConfig is null || string.IsNullOrWhiteSpace(artifactConfig.Location))
        {
            throw new RuntimeConfigFileWriteException
            (
                $"Cannot render runtime-config-file for app '{app.Slug}': "
                + "artifact location is not configured."
            );
        }

        var validationError = ValidatePath(config.Path);

        if (validationError is not null)
        {
            throw new RuntimeConfigFileWriteException
            (
                $"Cannot render runtime-config-file for app '{app.Slug}': {validationError}"
            );
        }

        if (!Directory.Exists(artifactConfig.Location))
        {
            throw new RuntimeConfigFileWriteException
            (
                $"Cannot render runtime-config-file for app '{app.Slug}': "
                + $"artifact directory does not exist at '{artifactConfig.Location}'. "
                + "Deploy the app's files before non-empty values can be applied."
            );
        }

        var targetPath = ResolveTargetPath(artifactConfig.Location, config.Path);

        var json = JsonSerializer.Serialize
        (
            config.Values,
            _jsonOptions
        );

        var targetDirectory = Path.GetDirectoryName(targetPath);

        if (!string.IsNullOrEmpty(targetDirectory) && !Directory.Exists(targetDirectory))
        {
            throw new RuntimeConfigFileWriteException
            (
                $"Cannot render runtime-config-file for app '{app.Slug}': "
                + $"target directory does not exist at '{targetDirectory}'."
            );
        }

        // Atomic write: render to temp in the same directory, then move with
        // overwrite. Same-directory temp keeps the move atomic (rename(2) on
        // Linux; MoveFileEx with MOVEFILE_REPLACE_EXISTING on Windows -- the
        // latter is best-effort-atomic, not strictly atomic, but the window is
        // small enough that no portal client should observe it).
        var tempPath = targetPath + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            await File.WriteAllTextAsync(tempPath, json, ct);
            File.Move(tempPath, targetPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDeleteTemp(tempPath);

            throw new RuntimeConfigFileWriteException
            (
                $"Failed to write runtime-config-file for app '{app.Slug}' to '{targetPath}': "
                + ex.Message,
                ex
            );
        }

        _logger.LogInformation
        (
            "Wrote runtime-config-file for app {Slug} to {TargetPath} ({KeyCount} keys)",
            app.Slug,
            targetPath,
            config.Values.Count
        );
    }

    // Reads the existing file on disk (if any) and returns its flat string->string
    // entries plus a list of skipped non-flat keys (nested objects, arrays, nulls,
    // non-string primitives). Used by the importer endpoint to pre-populate
    // operator overrides from an existing config.json.
    //
    // Bill ruling, S55 #6: importer ships in v1, flat-JSON only, surfaces a
    // warning naming skipped non-flat entries.
    public async Task<RuntimeConfigFileImportResult> ImportFromDiskAsync
    (
        App app,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(app);

        var config = await _capabilityStore.ResolveAsync<RuntimeConfigFileConfiguration>
        (
            "runtime-config-file", app, ct
        ) ?? throw new RuntimeConfigFileWriteException
        (
            $"App '{app.Slug}' does not declare the runtime-config-file capability."
        );

        var artifactConfig = await _capabilityStore.ResolveAsync<ArtifactConfiguration>
        (
            "artifact", app, ct
        );

        if (artifactConfig is null || string.IsNullOrWhiteSpace(artifactConfig.Location))
        {
            throw new RuntimeConfigFileWriteException
            (
                $"Cannot import runtime-config-file for app '{app.Slug}': "
                + "artifact location is not configured."
            );
        }

        var validationError = ValidatePath(config.Path);

        if (validationError is not null)
        {
            throw new RuntimeConfigFileWriteException
            (
                $"Cannot import runtime-config-file for app '{app.Slug}': {validationError}"
            );
        }

        var sourcePath = ResolveTargetPath(artifactConfig.Location, config.Path);

        if (!File.Exists(sourcePath))
        {
            throw new RuntimeConfigFileWriteException
            (
                $"No file found at '{sourcePath}' to import for app '{app.Slug}'."
            );
        }

        string text;

        try
        {
            text = await File.ReadAllTextAsync(sourcePath, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new RuntimeConfigFileWriteException
            (
                $"Failed to read '{sourcePath}' for app '{app.Slug}': {ex.Message}",
                ex
            );
        }

        JsonNode? parsed;

        try
        {
            parsed = JsonNode.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new RuntimeConfigFileWriteException
            (
                $"File at '{sourcePath}' is not a JSON object: {ex.Message}",
                ex
            );
        }

        // Funnel non-object roots (JsonArray, JsonValue, or top-level null) to the
        // same 400-with-actionable-message path. Casting via .AsObject() would
        // throw InvalidOperationException on a JsonArray / JsonValue, which is
        // NOT a JsonException, and would surface as a 500 from the endpoint.
        if (parsed is not JsonObject root)
        {
            throw new RuntimeConfigFileWriteException
            (
                $"File at '{sourcePath}' is not a JSON object."
            );
        }

        var imported = new Dictionary<string, string>(StringComparer.Ordinal);
        var skipped = new List<string>();

        foreach (var (key, value) in root)
        {
            if (value is JsonValue jsonValue
                && jsonValue.TryGetValue<string>(out var stringValue))
            {
                imported[key] = stringValue;
            }
            else
            {
                skipped.Add(key);
            }
        }

        return new RuntimeConfigFileImportResult(imported, skipped, sourcePath);
    }

    // Returns null when the path is valid; otherwise a human-readable reason.
    // Rejects absolute paths and any ".." segment to prevent escape from the
    // artifact directory. Leading '/' is permitted as a convention -- it's
    // treated as a path-relative-to-artifact-root in ResolveTargetPath.
    private static string? ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "path must not be empty.";
        }

        // Reject Windows-style drive roots (e.g. "C:\...").
        if (path.Length >= 2 && path[1] == ':')
        {
            return $"path must be relative to the artifact directory, not absolute ('{path}').";
        }

        // Reject UNC paths.
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return $"path must be relative to the artifact directory, not a UNC path ('{path}').";
        }

        // Tokenize and check for ".." segments. The leading '/' (if present)
        // produces an empty first segment, which is fine.
        var segments = path.Split(['/', '\\'], StringSplitOptions.None);

        foreach (var segment in segments)
        {
            if (string.Equals(segment, "..", StringComparison.Ordinal))
            {
                return $"path must not contain '..' segments ('{path}').";
            }
        }

        return null;
    }

    private static string ResolveTargetPath(string artifactLocation, string relativePath)
    {
        // Strip a single leading '/' or '\\' so Path.Combine treats the path as
        // relative to the artifact directory (Path.Combine would otherwise
        // return the absolute path).
        var stripped = relativePath.TrimStart('/', '\\');

        return Path.Combine(artifactLocation, stripped);
    }

    private void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to clean up temp file {TempPath}", tempPath);
        }
    }
}

public class RuntimeConfigFileWriteException : Exception
{
    public RuntimeConfigFileWriteException(string message) : base(message) { }

    public RuntimeConfigFileWriteException(string message, Exception innerException)
        : base(message, innerException) { }

    public RuntimeConfigFileWriteException() { }
}

public record RuntimeConfigFileImportResult
(
    IReadOnlyDictionary<string, string> Imported,
    IReadOnlyList<string> Skipped,
    string SourcePath
);
