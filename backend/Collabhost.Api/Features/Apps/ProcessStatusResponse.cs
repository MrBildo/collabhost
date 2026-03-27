using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Services;

namespace Collabhost.Api.Features.Apps;

public record ProcessStatusResponse
(
    string ExternalId,
    string AppName,
    string ProcessState,
    int? Pid,
    DateTime? StartedAt,
    double? UptimeSeconds,
    int RestartCount,
    DateTime? LastRestartAt
);

internal static class ProcessStatusMapper
{
    internal static ProcessStatusResponse Map(ManagedProcess process) =>
        new
        (
            process.AppExternalId,
            process.AppName,
            ResolveStateName(process.ProcessStateId),
            process.Pid,
            process.StartedAt,
            process.UptimeSeconds,
            process.RestartCount,
            process.LastRestartAt
        );

    internal static ProcessStatusResponse Stopped(string externalId, string appName) =>
        new
        (
            externalId,
            appName,
            StringCatalog.ProcessStates.Stopped,
            null,
            null,
            null,
            0,
            null
        );

    private static string ResolveStateName(Guid stateId) => stateId switch
    {
        _ when stateId == IdentifierCatalog.ProcessStates.Stopped => StringCatalog.ProcessStates.Stopped,
        _ when stateId == IdentifierCatalog.ProcessStates.Starting => StringCatalog.ProcessStates.Starting,
        _ when stateId == IdentifierCatalog.ProcessStates.Running => StringCatalog.ProcessStates.Running,
        _ when stateId == IdentifierCatalog.ProcessStates.Stopping => StringCatalog.ProcessStates.Stopping,
        _ when stateId == IdentifierCatalog.ProcessStates.Crashed => StringCatalog.ProcessStates.Crashed,
        _ when stateId == IdentifierCatalog.ProcessStates.Restarting => StringCatalog.ProcessStates.Restarting,
        _ => "Unknown"
    };
}
