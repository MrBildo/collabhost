namespace Collabhost.Api.Registry;

// The command + outcome shapes of the Registry subsystem's operations (code-structure-conventions
// §7: a subsystem's command shapes live in a *Contracts.cs grouping file, not stranded in a
// surface file). The operation spine's contracts file -- distinct from _ApiContracts.cs (the REST
// wire DTOs) by cohesion: these are the surface-agnostic command/outcome vocabulary the
// IOperation<,> spine speaks, which both the REST endpoint and the MCP tool adapt to and from.
// PRs 3-7 of the #406 arc add their operations' commands here (start/stop/settings/create/delete).

// The normalized input each lifecycle operation receives. The surface (REST route arg, MCP tool
// arg) adapts its raw input into the command; for the process-only lifecycle operations the only
// input is the slug, already normalized.
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
