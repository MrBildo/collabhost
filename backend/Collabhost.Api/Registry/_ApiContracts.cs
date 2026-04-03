using Collabhost.Api.Capabilities;

namespace Collabhost.Api.Registry;

// JSON-serialized DTOs -- List<T> and Dictionary<K,V> are practical for these response types
#pragma warning disable MA0016

// --- App List ---

public sealed record AppListItem
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

public sealed record AppTypeRef(string Name, string DisplayName);

public sealed record AppListActions(bool CanStart, bool CanStop);

// --- App Detail ---

public sealed record AppDetail
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
    List<AppTag> Tags,
    AppResources? Resources,
    AppRoute? Route,
    AppActions Actions
);

public sealed record AppTypeDetailRef(string Id, string Name, string DisplayName);

public sealed record AppTag(string Label, string Group);

public sealed record AppResources(double? CpuPercent, double? MemoryMb, int? HandleCount);

public sealed record AppRoute(string Domain, string Target, bool Tls);

public sealed record AppActions
(
    bool CanStart,
    bool CanStop,
    bool CanRestart,
    bool CanKill,
    bool CanUpdate
);

// --- App Settings ---

public sealed record AppSettings
(
    string Id,
    string Name,
    string DisplayName,
    string AppTypeName,
    string RegisteredAt,
    List<SettingsSection> Sections
);

public sealed record SettingsSection
(
    string Key,
    string Title,
    List<SettingsField> Fields
);

public sealed record SettingsField
(
    string Key,
    string Label,
    string Type,
    object? Value,
    object? DefaultValue,
    FieldEditable Editable,
    List<FieldOption>? Options = null,
    string? HelpText = null,
    string? Unit = null
);

// --- Action Result ---

public sealed record AppActionResult
(
    string Id,
    string Status,
    AppActions Actions
);

// --- Logs ---

public sealed record LogsResponse
(
    List<LogEntryResponse> Entries,
    int TotalBuffered
);

public sealed record LogEntryResponse
(
    string Timestamp,
    string Stream,
    string Content,
    string? Level
);

// --- Create ---

public sealed record CreateAppRequest
(
    string Name,
    string DisplayName,
    string AppTypeId,
    Dictionary<string, Dictionary<string, JsonElement>>? Values
);

public sealed record CreateAppResponse(string Id);

// --- Update Settings ---

public sealed record UpdateSettingsRequest
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
            _ => state.ToString().ToLowerInvariant()
        };
    }
}
