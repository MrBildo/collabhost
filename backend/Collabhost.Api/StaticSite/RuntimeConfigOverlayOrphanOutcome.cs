namespace Collabhost.Api.StaticSite;

public enum RuntimeConfigOverlayOrphanStatus
{
    Ok,
    OrphansFound
}

// One orphaned app: the overlay route is active and values are registered, but
// the overlay file is missing from the writable data dir. ExpectedFilePath is
// the writable-dir target RuntimeConfigFileWriter.RenderAsync would write to --
// surfaced so the operator-facing warning can name the exact path that 404s.
public record RuntimeConfigOverlayOrphan
(
    string Slug,
    string ExpectedFilePath
);

public record RuntimeConfigOverlayOrphanOutcome
(
    RuntimeConfigOverlayOrphanStatus Status,
    IReadOnlyList<RuntimeConfigOverlayOrphan> Orphans
);
