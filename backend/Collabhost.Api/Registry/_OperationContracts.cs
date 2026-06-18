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
// The three bool flags carry per-call command input -- they are not feature toggles smeared into
// the operation, they are what each surface asks the operation to do, made visible in each
// adapter's command construction:
//
//   - ValidateMergedOverrides: run CapabilityResolver.ValidateMergedOverrides on the post-merge
//     effective override (the #365-era two-step-operator-path cross-field check that rejects the
//     HSTS double-emission collision). BOTH adapters now pass true (#406 settings parity-fix).
//
//   - RefreshProbesOnArtifactChange: re-probe (InvalidateProbeCache + RunProbesAsync) when the
//     `artifact` section changes. BOTH adapters now pass true (#406 settings parity-fix).
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
// The first two flags WERE a confirmed REST<->MCP drift (the exact #350/#365/#366 hand-sync class
// §8 exists to delete): the pre-migration MCP update_settings path ran NEITHER the merged-overrides
// validation NOR the probe-refresh that REST ran. PR 5 migrated both surfaces onto this operation
// byte-for-byte, isolating the drift behind these two flags (REST true, MCP false) so the fix would
// be a visible diff. The #406 settings parity-fix then flipped the MCP flags to true -- the one
// sanctioned behavior change of the spine arc, per the operator ruling -- so both surfaces now share
// the merged-validation and probe-refresh. RejectUnknownSection is NOT a parity candidate: REST's
// skip vs MCP's reject is intended surface ergonomics (MCP fails loud for agents; REST skips for the
// typed UI), a genuine kept divergence.
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

// --- Create ---

// The normalized input CreateAppOperation receives. The two registration surfaces have very
// different RAW inputs (REST's typed nested dictionary with a "discovery" virtual section; MCP's
// `name` + flat `installDirectory` + raw JSON-string `settings`), and the genuine REST<->MCP
// divergence is ENTIRELY at the input edge -- each surface ASSEMBLES its raw input into this one
// normalized command, and the shared operation core is byte-identical across both. There are NO
// flags (unlike UpdateSettingsCommand): Create/Register has no mid-body divergence -- the create
// sequence (exists -> type -> validate -> create -> save -> route -> events -> hints) is identical
// on both surfaces, so the divergence is fully resolved at the adapters and nothing rides the
// command to gate in-body behavior.
//
//   - Slug: a VALID slug by construction -- the adapter validated (REST: Slug.Validate(request.Name))
//     or derived-then-validated (MCP: name -> lowercase -> spaces-to-hyphens -> Slug.Validate) it,
//     and it carries the FINAL PERSISTED form (REST request.Name.Trim().ToLowerInvariant(); MCP the
//     already-lowercased derivedSlug). The operation does NOT re-run Slug.Validate or re-transform --
//     it asserts uniqueness (ExistsBySlugAsync) and creates with this slug verbatim. The slug-shape
//     check + the derive transform are the single-surface concerns that stay at each surface (the
//     derive-vs-take-as-given asymmetry and its divergent error prose live at the MCP adapter).
//   - DisplayName: REST takes it as-given; MCP uses name.Trim() (the MCP `name` IS the display name).
//   - AppTypeSlug: the slug string, NOT a resolved AppType -- the operation does the GetBySlug lookup
//     itself (shared core: both surfaces look it up and reject null, so the lookup + its not-found
//     mapping live once in the operation).
//   - Overrides: the RAW, section-assembled (but NOT yet validated) JsonObject keyed by capability
//     section ("process", "artifact", "routing", "external-target", ...), each value a JsonObject of
//     field->value. Validation runs ONCE inside the operation (CapabilityResolver.ValidateEdits,
//     isNewApp: true) -- the same placement UpdateSettingsOperation uses. The adapter's job is to
//     PRODUCE this object (REST folds its "discovery" section into "process"; MCP injects
//     installDirectory into process.workingDirectory / artifact.location); the operation VALIDATES
//     and persists it. This matches UpdateSettingsCommand.Changes' JsonObject-of-sections shape.
public sealed record CreateAppCommand
(
    string Slug,
    string DisplayName,
    string AppTypeSlug,
    JsonObject Overrides
);

// The surface-agnostic outcome of a registration. Carries the id, the slug, and the helpful-next-
// steps hints. The hints are computed ONCE in the operation -- folding the ~40-line
// ResolveHelpfulNextSteps method that was duplicated near-verbatim across BOTH surface files
// (AppRegistrationEndpoints + RegistrationTools) into the shared core (the §8 dedup target). Each
// surface emits the hint list verbatim (REST -> CreateAppResponse.HelpfulNextSteps; MCP ->
// helpfulNextSteps). writableDataPath is NOT on the outcome -- it is dataPathResolver.ResolveFor(slug),
// a trivial pure call each surface makes from outcome.Slug (both surfaces hold AppDataPathResolver).
// Co-located with the commands by cohesion (§7), the same way AppActionOutcome / UpdateSettingsOutcome
// are.
public sealed record CreateAppOutcome
(
    Ulid Id,
    string Slug,
    IReadOnlyList<string> Hints
);
