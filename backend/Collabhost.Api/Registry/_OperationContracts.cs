namespace Collabhost.Api.Registry;

// The command + outcome shapes of the Registry subsystem's operations (code-structure-conventions
// §7: a subsystem's command shapes live in a *Contracts.cs grouping file, not stranded in a
// surface file). The operation spine's contracts file -- distinct from _ApiContracts.cs (the REST
// wire DTOs) by cohesion: these are the surface-agnostic command/outcome vocabulary the
// IOperation<,> spine speaks, which both the REST endpoint and the MCP tool adapt to and from.
// PRs 3-7 of the #406 arc add their operations' commands here (start/stop/settings/create/delete).

// The normalized input each lifecycle operation receives. The surface (REST route arg, MCP tool
// arg) adapts its raw input into the command; for the lifecycle operations the only input is the
// slug, already normalized.
public sealed record StartAppCommand(string Slug);

public sealed record StopAppCommand(string Slug);

public sealed record RestartAppCommand(string Slug);

public sealed record KillAppCommand(string Slug);

// The surface-agnostic outcome of a lifecycle action -- carries everything BOTH surfaces need to
// build their own result, so neither touches the App entity after the operation returns. The REST
// endpoint maps it to AppActionResult (id + status + the BuildActions affordances); the MCP tool
// maps it to { slug, status, appType }. The action/appType divergence is preserved at each surface,
// never smeared into the operation. Not a *Command/*Request/*Response/*Arguments record by suffix:
// it is the operation's output vocabulary, co-located with the commands by cohesion (§7), the same
// way a composed item moves with the response it composes.
public sealed record AppActionOutcome
(
    Ulid Id,
    string Slug,
    string AppTypeSlug,
    ProcessState State,
    bool HasProcess,
    bool HasRouting
);

// The normalized input UpdateSettingsOperation receives. Both surfaces adapt their raw input into
// it (REST adapts UpdateSettingsRequest's typed nested dictionary; MCP parses its raw `settings`
// JSON string), and into the same JsonObject the operation's shared validate -> merge -> save loop
// walks.
//
// The two bool flags carry the GENUINE pre-migration REST<->MCP divergence as per-call command
// input -- they are not feature toggles smeared into the operation, they are what each surface
// asks the operation to do, made visible in each adapter's command construction:
//
//   - ValidateMergedOverrides: REST runs CapabilityResolver.ValidateMergedOverrides on the
//     post-merge effective override (the #365-era two-step-operator-path cross-field check); the
//     pre-migration MCP path NEVER did. REST adapter passes true, MCP adapter passes false.
//
//   - RefreshProbesOnArtifactChange: REST re-probes (InvalidateProbeCache + RunProbesAsync) when
//     the `artifact` section changes; the pre-migration MCP path NEVER did. REST adapter passes
//     true, MCP adapter passes false.
//
//   - RejectUnknownSection: an unknown capability section is SKIPPED by REST (continue) but is a
//     mid-loop hard error on MCP (return InvalidParameters). The single-surface-guard precedent
//     (PR 2/3) keeps a one-surface guard AT the surface -- but this guard fires INSIDE the
//     per-section loop, after preceding (known) sections in document order are already saved, so it
//     cannot move to a pre-call adapter check without changing MCP's "save-up-to-the-unknown-then-
//     stop" order. It therefore rides the command as a flag, gating the same in-loop position.
//     REST adapter passes false (skip), MCP adapter passes true (reject). The reject MESSAGE wording
//     ("Use get_settings to see valid sections...") stays at the MCP adapter -- the operation
//     returns OperationResult.Validation with the section name, the adapter shapes the prose.
//
// The first two are a confirmed REST<->MCP drift (the exact #350/#365/#366 hand-sync class §8
// exists to delete), surfaced -- NOT fixed -- in this PR: PR 5's mandate is byte-for-byte
// preservation on each surface; the one sanctioned parity-fix in the arc is PR 7's DeleteApp
// InvalidateProbeCache fold. Flipping both MCP drift-flags to true (matching REST) is the eventual
// one-line parity fix; the flags make that future decision a visible diff rather than a buried
// behavioral change. RejectUnknownSection is a genuine kept divergence (REST's skip vs MCP's
// reject is intended surface ergonomics, not drift), not a parity-fix candidate.
public sealed record UpdateSettingsCommand
(
    string Slug,
    JsonObject Changes,
    bool ValidateMergedOverrides,
    bool RefreshProbesOnArtifactChange,
    bool RejectUnknownSection
);

// The surface-agnostic outcome of a settings update -- carries only the slug, because each surface
// builds its OWN success result from it: the REST endpoint re-fetches the app and rebuilds the full
// AppSettings sections (BuildSettingsSections is REST result-mapping the endpoint owns); the MCP
// tool maps to its fixed "settings updated" message. The save already happened inside the operation,
// so neither surface needs anything but the slug to produce its result. Co-located with the commands
// by cohesion (§7), the same way AppActionOutcome is.
public sealed record UpdateSettingsOutcome(string Slug);
