using Collabhost.Api.Supervisor;

namespace Collabhost.Api.Registry;

// Single source of truth for an app's effective status, derived from its process state and
// routing state. Previously duplicated byte-for-byte across AppEndpoints, DiscoveryTools (MCP),
// and DashboardEndpoints; centralized here (#109) so the REST list, the dashboard strip, and the
// MCP discovery surface can never silently diverge on what "running" means for a given
// App + ManagedProcess + routing triple.
internal static class AppStatusResolver
{
    internal static ProcessState Resolve
    (
        bool hasProcess,
        ManagedProcess? process,
        bool hasRouting,
        bool routeEnabled
    ) =>
        Resolve(hasProcess, process?.State, hasRouting, routeEnabled);

    // Snapshot-friendly overload (Card #428). The detail-builder reads a single coherent
    // ProcessStateSnapshot and derives status from snapshot.State, so it never re-reads
    // process.State a second time. The ManagedProcess? overload above delegates here, so
    // the list / dashboard paths stay byte-identical.
    internal static ProcessState Resolve
    (
        bool hasProcess,
        ProcessState? processState,
        bool hasRouting,
        bool routeEnabled
    ) =>
        hasProcess
            ? processState ?? ProcessState.Stopped
            : hasRouting && routeEnabled
                ? ProcessState.Running
                : ProcessState.Stopped;
}
