namespace Collabhost.Api.Proxy;

// The command + outcome shapes of the Proxy subsystem's operations (code-structure-conventions
// §7: a subsystem's command shapes live in a *Contracts.cs grouping file, not stranded in a
// surface file; §9: the operation lives in its owning subsystem -- ReloadProxy is the first spine
// op outside Registry/). Distinct from the Registry lifecycle contracts: ReloadProxy is the
// trivial app-less operation, so its command and outcome diverge from the lifecycle template in
// two deliberate ways.

// The normalized input the reload operation receives. A marker (parameterless) record -- unlike
// the lifecycle commands it carries NO slug, because a proxy reload acts on no app: it regenerates
// the whole route table from the current registry state. The surface has nothing to adapt into it.
public sealed record ReloadProxyCommand;

// The surface-agnostic outcome of a proxy reload. A marker (empty) record -- unlike the lifecycle
// AppActionOutcome it carries NO fields, because a reload returns no per-app data either surface
// needs: the REST endpoint maps Success to 204 No Content, the MCP tool maps it to a fixed
// "reload requested" message. Both map the typed Success arm without reading a value. Not a
// *Command/*Request/*Response/*Arguments record by suffix: it is the operation's output vocabulary,
// co-located with the command by cohesion (§7).
//
// S2094 (empty record) is a genuine false positive here: the emptiness is the point. TResult on
// IOperation<TCommand, TResult> must be SOME type, and a reload has no payload, so the honest,
// domain-named "success carries no value" marker is exactly an empty record. Filling it with a
// dummy field, or adding a shared Unit type to the spine for one operation, would be ceremony.
#pragma warning disable S2094 // Intentional empty marker -- the surface-agnostic "no payload" outcome
public sealed record ProxyReloadOutcome;
#pragma warning restore S2094
