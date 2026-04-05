namespace Collabhost.Api.Supervisor;

// Allows subsystems to augment process arguments at start time.
// The supervisor calls each registered provider before creating the process.
// This exists so the proxy subsystem can inject the dynamic admin port
// into Caddy's arguments without persisting session-scoped values in the database.
public interface IProcessArgumentProvider
{
    string? AugmentArguments(string appSlug, string? resolvedArguments);
}
