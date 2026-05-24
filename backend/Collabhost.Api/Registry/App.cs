namespace Collabhost.Api.Registry;

public class App
{
    public Ulid Id { get; init; } = Ulid.NewUlid();

    public required string Slug { get; init; }

    public required string DisplayName { get; set; }

    public required string AppTypeSlug { get; init; }

    public DateTime RegisteredAt { get; init; } = DateTime.UtcNow;

    public DateTime? ModifiedAt { get; set; }

    // Persisted operator-stop intent. Mirrors the in-memory ManagedProcess.StoppedByOperator
    // flag onto persistence so the state survives Collabhost restart. The in-memory flag
    // keeps its within-lifetime crash-suppression role; this column is its persistent peer.
    // Boot consumers: ProcessSupervisor.StartAsync (auto-start suppression for process-bearing
    // types) and ProxyManager.HydrateRouteStatesFromPersistenceAsync (route-state restoration
    // for routing-only types). Card #350.
    public bool StoppedByOperator { get; set; }
}
