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
    List<ProbeEntry> Probes,
    AppResources? Resources,
    AppRoute? Route,
    AppActions Actions
);

public record AppTypeDetailRef(string Slug, string DisplayName);

// AppTag is used by AppType contracts (type-level metadata).
// Removed from AppDetail -- replaced by probe system (card #68).
public record AppTag(string Label, string Group);

public record AppResources(double? CpuPercent, double? MemoryMb, int? HandleCount);

public record AppRoute(string Domain, string Target, bool Tls);

public record AppActions
(
    bool CanStart,
    bool CanStop,
    bool CanRestart,
    bool CanKill,
    bool CanUpdate
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
    string? Unit = null
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

public record CreateAppResponse(string Id);

// --- Update Settings ---

public record UpdateSettingsRequest
(
    Dictionary<string, Dictionary<string, JsonElement>> Changes
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
}
