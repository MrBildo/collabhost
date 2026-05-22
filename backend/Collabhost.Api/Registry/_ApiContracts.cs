using Collabhost.Api.Capabilities;
using Collabhost.Api.Probes;

namespace Collabhost.Api.Registry;

// JSON-serialized DTOs -- List<T> and Dictionary<K,V> are practical for these response types
#pragma warning disable MA0016
// --- App List ---

public record AppListItem
(
    string Id,
    string Name,
    string DisplayName,
    AppTypeRef AppType,
    string Status,
    string? Domain,
    bool DomainActive,
    int? Port,
    double? UptimeSeconds,
    AppListActions Actions
);

public record AppTypeRef(string Slug, string DisplayName);

public record AppListActions(bool CanStart, bool CanStop);

// --- App Detail ---

public record AppDetail
(
    string Id,
    string Name,
    string DisplayName,
    AppTypeDetailRef AppType,
    string RegisteredAt,
    string Status,
    int? Pid,
    int? Port,
    double? UptimeSeconds,
    int RestartCount,
    string? RestartPolicy,
    bool? AutoStart,
    string? Domain,
    bool DomainActive,
    string? HealthStatus,
    // Probe cache lifecycle state (Card #337). Distinguishes never-probed /
    // fresh / stale / not-applicable from each other; before #337 all four
    // collapsed to an empty Probes array, leaving the frontend unable to
    // render distinct empty-state copy. Values: "fresh", "stale",
    // "never-probed", "not-applicable" (lowercase-hyphen).
    string ProbesStatus,
    List<ProbeEntry> Probes,
    AppResources? Resources,
    AppRoute? Route,
    AppActions Actions,
    // Per-app writable data path (#326 / #322 decision E1). Absolute path,
    // runtime-derived from COLLABHOST_DATA_PATH, never persisted. The operator
    // points the app's writable config (e.g. a SQLite connection string) at
    // this path so it lands inside the system-scope unit's ReadWritePaths.
    string WritableDataPath
);

public record AppTypeDetailRef(string Slug, string DisplayName);

public record AppResources(double? CpuPercent, double? MemoryMb, int? HandleCount);

public record AppRoute(string Domain, string Target, bool Tls);

public record AppActions
(
    bool CanStart,
    bool CanStop,
    bool CanRestart,
    bool CanKill
);

// --- App Settings ---

public record AppSettings
(
    string Id,
    string Name,
    string DisplayName,
    string AppTypeName,
    string RegisteredAt,
    List<SettingsSection> Sections
);

public record SettingsSection
(
    string Key,
    string Title,
    List<SettingsField> Fields
);

public record SettingsField
(
    string Key,
    string Label,
    string Type,
    object? Value,
    object? DefaultValue,
    FieldEditable Editable,
    bool RequiresRestart = false,
    List<FieldOption>? Options = null,
    string? HelpText = null,
    string? Unit = null,
    // KeyValue-only. The regex a key must satisfy plus the operator-facing
    // message. Absent => the frontend keeps its existing env-var key default
    // (so every existing env-var KeyValue field is byte-for-byte unaffected).
    // Server-authoritative: CapabilityResolver.ValidateEdits enforces the same
    // pattern; this carries it to the frontend as a mirror. Card #308.
    string? KeyPattern = null,
    string? KeyPatternMessage = null
);

// --- Action Result ---

public record AppActionResult
(
    string Id,
    string Status,
    AppActions Actions
);

// --- Logs ---

public record LogsResponse
(
    List<LogEntryResponse> Entries,
    int TotalBuffered
);

public record LogEntryResponse
(
    long Id,
    string Timestamp,
    string Stream,
    string Content,
    string? Level
);

// --- Create ---

public record CreateAppRequest
(
    string Name,
    string DisplayName,
    string AppTypeSlug,
    Dictionary<string, Dictionary<string, JsonElement>>? Values
);

// WritableDataPath (#326 / #322 decision E1): the per-app writable data path,
// surfaced on the registration response so the operator can configure the
// app's writable state location (absolute, inside ReadWritePaths) without
// deriving it by hand. Absolute path, runtime-derived, never persisted.
public record CreateAppResponse(string Id, string WritableDataPath);

// --- Update Settings ---

public record UpdateSettingsRequest
(
    Dictionary<string, Dictionary<string, JsonElement>> Changes
);

// --- Runtime Config File Import (Card #336) ---

// Response from POST /apps/{slug}/runtime-config-file/import. The operator
// reviews `imported` (the flat string->string entries pulled from the existing
// file on disk) and saves them via the standard settings-save flow. `skipped`
// names any top-level entries that were non-flat (nested objects, arrays,
// nulls, non-string primitives) -- the importer surfaces them so the operator
// can decide whether to manage those via the source file or convert them.
public record RuntimeConfigFileImportResponse
(
    IReadOnlyDictionary<string, string> Imported,
    IReadOnlyList<string> Skipped,
    string SourcePath
);

// --- ProcessState extension ---

public static class ProcessStateExtensions
{
    extension(ProcessState state)
    {
        public string ToApiString() => state switch
        {
            ProcessState.Stopped => "stopped",
            ProcessState.Starting => "starting",
            ProcessState.Running => "running",
            ProcessState.Stopping => "stopping",
            ProcessState.Crashed => "crashed",
            ProcessState.Restarting => "restarting",
            ProcessState.Backoff => "backoff",
            ProcessState.Fatal => "fatal",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown ProcessState value"),
        };
    }

    extension(ProbeCacheStatus status)
    {
        public string ToApiString() => status switch
        {
            ProbeCacheStatus.Fresh => "fresh",
            ProbeCacheStatus.Stale => "stale",
            ProbeCacheStatus.NeverProbed => "never-probed",
            ProbeCacheStatus.NotApplicable => "not-applicable",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown ProbeCacheStatus value"),
        };
    }
}
