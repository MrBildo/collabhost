using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Installation;
using Collabhost.Api.Operations;
using Collabhost.Api.Proxy;
using Collabhost.Api.Shared;

namespace Collabhost.Api.Registry;

// Register a new app (code-structure-conventions §8/§9 -- a concrete operation in its owning
// subsystem). THE divergence operation of the arc: the two registration surfaces have very
// different RAW inputs (REST's typed nested dictionary with a "discovery" virtual section; MCP's
// `name` + flat `installDirectory` + raw JSON-string `settings`), and both re-implemented this body
// near-line-for-line and hand-synced it (#345/#348) -- the exact REST<->MCP drift §8 deletes.
//
// The genuine divergence is ENTIRELY at the input edge: each surface ASSEMBLES its raw input into a
// normalized CreateAppCommand (REST folds "discovery" into "process"; MCP injects installDirectory
// into process.workingDirectory / artifact.location; the surface-only slug derive/validate and the
// MCP directoryRequired gate stay at each surface). After assembly the operation sees two identical
// commands and its body is surface-blind -- exists -> type -> validate -> create -> save -> route ->
// events -> hints. There are NO flags (unlike UpdateSettingsOperation): Create/Register has no mid-
// body divergence at all, so the divergence is fully resolved at the adapters and nothing rides the
// command to gate in-body behavior.
//
// The body is intent only: exists-check -> type-lookup -> per-section validate -> create -> save
// overrides -> route toggle -> record event(s) -> compute hints -> shape outcome. No try/catch-to-
// result (no supervisor call here; nothing throws InvalidOperationException), no hand-built
// ActivityEvent (the base RecordAsync helpers stamp the actor + carry the metadata).
//
// The 1-vs-2-event conditional: a normal registration records ONE app.created event; an external-
// route (hasExternalTarget) auto-enables its route at registration (Card #348, D8) and records BOTH
// app.created AND app.started so the activity feed reflects the route going live -- the same event
// the manual start path records for routing-only apps. Preserved exactly: both pre-migration bodies
// recorded the second app.started event only on the hasExternalTarget branch.
//
// ResolveHelpfulNextSteps (the ~40-line method that was duplicated near-verbatim in BOTH surface
// files) folds in here as a private helper -- a real §8 dedup. Verified char-for-char identical
// across the two pre-migration copies modulo field-vs-parameter reference (REST took TypeStore +
// ProxySettings as params; MCP read the _typeStore / _proxySettings fields -- the same DI singletons).
public sealed class CreateAppOperation
(
    AppStore store,
    TypeStore typeStore,
    ProxyManager proxy,
    ProxySettings proxySettings,
    ExternalTargetSettings externalTargetSettings,
    ICurrentUser currentUser,
    ActivityEventStore activityEventStore
) : Operation<CreateAppCommand, CreateAppOutcome>(currentUser, activityEventStore)
{
    // The override-serialization options. Value-equivalent for a JsonObject to both surfaces'
    // pre-migration options (REST's local _jsonOptions and MCP's McpResponseFormatter.JsonOptions):
    // JsonNode.ToJsonString ignores DefaultIgnoreCondition (it applies to POCO property serialization,
    // not to a JsonNode tree) and PropertyNameCaseInsensitive (a deserialization concern), so the
    // only behaviorally-relevant option -- the CamelCase naming policy + enum converter -- matches
    // both. The stored JSON is byte-identical to either pre-migration path. Mirrors
    // UpdateSettingsOperation._overrideSerializationOptions exactly (the same established analysis).
    private static readonly JsonSerializerOptions _overrideSerializationOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly AppStore _store = store
        ?? throw new ArgumentNullException(nameof(store));

    private readonly TypeStore _typeStore = typeStore
        ?? throw new ArgumentNullException(nameof(typeStore));

    private readonly ProxyManager _proxy = proxy
        ?? throw new ArgumentNullException(nameof(proxy));

    private readonly ProxySettings _proxySettings = proxySettings
        ?? throw new ArgumentNullException(nameof(proxySettings));

    // Card #348, D3. Threaded into CapabilityResolver.ValidateEdits when the section under edit is
    // external-target so the host-pattern check honors the operator's public-hosts opt-in.
    private readonly ExternalTargetSettings _externalTargetSettings = externalTargetSettings
        ?? throw new ArgumentNullException(nameof(externalTargetSettings));

    protected override async Task<OperationResult<CreateAppOutcome>> ExecuteCoreAsync
    (
        CreateAppCommand command,
        CancellationToken ct
    )
    {
        // Exists-check BEFORE type-lookup (Marcus design R3): both pre-migration bodies check
        // existence first, so a request with a duplicate slug AND a bad type returns the conflict
        // (REST 409), not the type-not-found -- preserved by running these in this order. The slug
        // is already the final persisted form (Marcus R7), so the exists-check and the later create
        // use the identical slug; the latent REST raw-name-exists-check-vs-normalized-persist
        // inconsistency is structurally impossible here.
        if (await _store.ExistsBySlugAsync(command.Slug, ct))
        {
            return OperationResult<CreateAppOutcome>.Conflict
            (
                $"An app with slug '{command.Slug}' already exists."
            );
        }

        var appType = _typeStore.GetBySlug(command.AppTypeSlug);

        if (appType is null)
        {
            return OperationResult<CreateAppOutcome>.NotFound("App type not found.");
        }

        // Validate all sections BEFORE creating the app so registration is transactional. Validation
        // runs ONCE here, not in the adapters (Marcus design §1.3) -- the same placement
        // UpdateSettingsOperation uses. The adapter already assembled the divergent input (REST's
        // discovery->process merge, MCP's installDirectory injection) into command.Overrides, so the
        // operation validates every section uniformly.
        //
        // #436: reject any capability section the resolved app type does not bind. Before this,
        // CapabilityResolver.ValidateEdits returned zero errors when CapabilityCatalog.GetSchema was
        // null for a section, so a junk section (a typo'd capability slug, an unbound capability)
        // validated clean and was persisted SILENTLY. Registration is strict-at-create on BOTH
        // surfaces (operator ruling): an unknown section is a hard failure here regardless of surface.
        // This is the inverse of the settings-EDIT loop, which stays lenient (REST skips, MCP rejects
        // mid-loop) -- only registration is strict. The membership test is the RESOLVED TYPE'S
        // bindings, not the builtin catalog, so a user-defined type's own declared sections pass and
        // only sections unknown to the resolved type reject; a catalog capability the type does not
        // bind (e.g. `process` on a static-site) is rejected too.
        var typeBindings = _typeStore.GetBindings(appType.Slug);

        var validatedOverrides = new List<(string SectionKey, JsonObject Overrides)>();

        foreach (var (sectionKey, sectionValueNode) in command.Overrides)
        {
            if (typeBindings is null || !typeBindings.ContainsKey(sectionKey))
            {
                var knownSections = typeBindings is { Count: > 0 }
                    ? string.Join(", ", typeBindings.Keys.Order(StringComparer.Ordinal))
                    : "none";

                return OperationResult<CreateAppOutcome>.Validation
                (
                    $"Unknown capability section '{sectionKey}' for app type '{appType.Slug}'. "
                    + $"Known sections for this type: {knownSections}."
                );
            }

            if (sectionValueNode is not JsonObject sectionObject)
            {
                continue;
            }

            var validationErrors = CapabilityResolver.ValidateEdits
            (
                sectionKey, sectionObject, true, _externalTargetSettings.AllowPublicHosts
            );

            if (validationErrors.Count > 0)
            {
                return OperationResult<CreateAppOutcome>.Validation(string.Join("; ", validationErrors));
            }

            validatedOverrides.Add((sectionKey, sectionObject));
        }

        var app = new App
        {
            Slug = command.Slug,
            DisplayName = command.DisplayName,
            AppTypeSlug = appType.Slug
        };

        // REG-02: persist the App row and every capability override in ONE transaction (the store
        // owns the commit). The prior shape created the App, then looped saving overrides, each in
        // its own context -- so a failure mid-loop left a half-configured app on disk. REG-04: the
        // exists-check above closes the common duplicate-slug case, but two concurrent creates of the
        // same slug can both pass the check and race to insert -- the loser hits the unique Slug
        // index and the store throws DbUpdateException. Map that to the same Conflict the exists-check
        // returns (a 409), not an unhandled 500.
        var overridesToPersist = validatedOverrides
            .Select(o => (o.SectionKey, o.Overrides.ToJsonString(_overrideSerializationOptions)))
                .ToList();

        try
        {
            await _store.CreateWithOverridesAsync(app, overridesToPersist, ct);
        }
        catch (DbUpdateException)
        {
            return OperationResult<CreateAppOutcome>.Conflict
            (
                $"An app with slug '{command.Slug}' already exists."
            );
        }

        // Routing-only apps (e.g. static sites) start with their route disabled because the operator
        // still needs to populate an artifact directory before the route is meaningful. External-route
        // apps (Card #348, D8) skip that intermediate step -- the upstream is operator-declared -- so
        // they auto-enable at registration. Process-based apps have routes tied to process state and
        // need no explicit toggle here. Byte-identical to both pre-migration bodies.
        var hasRouting = _typeStore.HasBinding(appType.Slug, "routing");
        var hasProcess = _typeStore.HasBinding(appType.Slug, "process");
        var hasExternalTarget = _typeStore.HasBinding(appType.Slug, "external-target");

        if (hasRouting && !hasProcess)
        {
            if (hasExternalTarget)
            {
                _proxy.EnableRoute(app.Slug);
                _proxy.RequestSync();
            }
            else
            {
                _proxy.DisableRoute(app.Slug);
            }
        }

        await RecordAsync
        (
            ActivityEventTypes.AppCreated,
            app.Id.ToCanonicalString(),
            app.Slug,
            JsonSerializer.Serialize(new { appTypeSlug = appType.Slug, displayName = app.DisplayName }),
            ct
        );

        // Card #348 polish (C-1): external-route apps auto-enable at registration (D8). Record
        // app.started so the activity feed reflects the route going live -- the same event the manual
        // start path records for routing-only apps. The 1-vs-2-event conditional: normal registration
        // records only app.created; an external-route records BOTH. Preserved exactly.
        if (hasExternalTarget)
        {
            await RecordAsync(ActivityEventTypes.AppStarted, app, ct);
        }

        // Card #345: surface a one-line `collabhost --update-hosts` hint, scoped to routed app types
        // (system-service / process-only stays silent -- no Caddy route, no hosts entry). The dedup of
        // the ~40-line method duplicated in both surface files (Marcus §2.1).
        var hints = ResolveHelpfulNextSteps(appType.Slug, app.Slug, validatedOverrides);

        return OperationResult<CreateAppOutcome>.Success(new CreateAppOutcome(app.Id, app.Slug, hints));
    }

    // The helpful-next-steps hint resolution, folded in from the two duplicated surface copies
    // (Marcus §2.1 dedup). Only routed app types get the hosts hint -- a system-service or process-
    // only registration never gets a Caddy route, so it stays silent. Resolves the effective domain
    // through the same CapabilityResolver path the routing surface uses so an operator-set
    // DomainPattern is reflected in the hint.
    private IReadOnlyList<string> ResolveHelpfulNextSteps
    (
        string appTypeSlug,
        string slug,
        IReadOnlyList<(string SectionKey, JsonObject Overrides)> validatedOverrides
    )
    {
        if (!_typeStore.HasBinding(appTypeSlug, "routing"))
        {
            return [];
        }

        var bindings = _typeStore.GetBindings(appTypeSlug);

        if (bindings is null || !bindings.TryGetValue("routing", out var routingBindingJson))
        {
            return [];
        }

        string? overrideJson = null;

        foreach (var (sectionKey, overrideObject) in validatedOverrides)
        {
            if (string.Equals(sectionKey, "routing", StringComparison.Ordinal))
            {
                overrideJson = overrideObject.ToJsonString();
                break;
            }
        }

        var routing = CapabilityResolver.Resolve<RoutingConfiguration>(routingBindingJson, overrideJson);

        var hostname = CapabilityResolver.ResolveDomain(routing.DomainPattern, slug, _proxySettings.BaseDomain);

        return [HostsHintBuilder.Compose(hostname)];
    }
}
