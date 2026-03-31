using Collabhost.Api.Domain.Catalogs;

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
    internal static async Task<ProcessStatusResponse> MapAsync
    (
        ManagedProcess process,
        IProcessStateNameResolver stateNameResolver,
        CancellationToken ct
    )
    {
        var stateName = await stateNameResolver.ResolveDisplayNameAsync(process.ProcessStateId, ct);

        return new ProcessStatusResponse
        (
            process.AppExternalId,
            process.AppName,
            stateName,
            process.Pid,
            process.StartedAt,
            process.UptimeSeconds,
            process.RestartCount,
            process.LastRestartAt
        );
    }

    internal static async Task<ProcessStatusResponse> StoppedAsync
    (
        string externalId,
        string appName,
        IProcessStateNameResolver stateNameResolver,
        CancellationToken ct
    )
    {
        var stoppedName = await stateNameResolver.ResolveDisplayNameAsync
        (
            IdentifierCatalog.ProcessStates.Stopped, ct
        );

        return new ProcessStatusResponse
        (
            externalId,
            appName,
            stoppedName,
            null,
            null,
            null,
            0,
            null
        );
    }
}
