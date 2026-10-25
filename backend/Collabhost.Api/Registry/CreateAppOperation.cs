using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Installation;
using Collabhost.Api.Operations;
using Collabhost.Api.Portal;
using Collabhost.Api.Proxy;
using Collabhost.Api.Shared;

namespace Collabhost.Api.Registry;

// Register a new app: the one create sequence both registration surfaces share, kept in the
// subsystem that owns apps. The two surfaces have very different RAW inputs (REST's typed nested
// dictionary with a "discovery" virtual section; MCP's `name` + flat `installDirectory` + raw
// JSON-string `settings`). Each once re-implemented this body near-line-for-line and the two were
// kept in step by hand, which is how REST and MCP drifted apart; one shared body removes that.
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
// route (hasExternalTarget) auto-enables its route at registration, because its upstream is
// operator-declared and there is nothing left to set up, and records BOTH app.created AND
// app.started so the activity feed reflects the route going live -- the same event the manual start
// path records for routing-only apps. Preserved exactly: both pre-migration bodies recorded the
// second app.started event only on the hasExternalTarget branch.
//
// ResolveHelpfulNextSteps (the ~40-line method that was duplicated near-verbatim in BOTH surface
// files) folds in here as a private helper, so it exists once. Verified char-for-char identical
// across the two pre-migration copies modulo field-vs-parameter reference (REST took TypeStore +
// ProxySettings as params; MCP read the _typeStore / _proxySettings fields -- the same DI singletons).
public sealed class CreateAppOperation
(
    AppStore store,
    TypeStore typeStore,
    CapabilityStore capabilityStore,
    ProxyManager proxy,
    ProxySettings proxySettings,
    PortalSettings portalSettings,
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

    private readonly CapabilityStore _capabilityStore = capabilityStore
        ?? throw new ArgumentNullException(nameof(capabilityStore));

    private readonly ProxyManager _proxy = proxy
        ?? throw new ArgumentNullException(nameof(proxy));

    private readonly ProxySettings _proxySettings = proxySettings
        ?? throw new ArgumentNullException(nameof(proxySettings));

    private readonly PortalSettings _portalSettings = portalSettings
        ?? throw new ArgumentNullException(nameof(portalSettings));

    // Threaded into CapabilityResolver.ValidateEdits when the section under edit is external-target,
    // so the host-pattern check honors the operator's opt-in to public upstream hosts.
    private readonly ExternalTargetSettings _externalTargetSettings = externalTargetSettings
        ?? throw new ArgumentNullException(nameof(externalTargetSettings));

    protected override async Task<OperationResult<CreateAppOutcome>> ExecuteCoreAsync
    (
        CreateAppCommand command,
        CancellationToken ct
    )
    {
        // Exists-check BEFORE type-lookup: both pre-migration bodies checked existence first, so a
        // request with a duplicate slug AND a bad type returns the conflict (REST 409), not the
        // type-not-found -- preserved by running these in this order. The slug arrives in its final
        // persisted form (each surface normalizes it), so the exists-check and the later create
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

        var refusal = DescribeRefusal(appType, _typeStore);

        if (refusal is not null)
        {
            return OperationResult<CreateAppOutcome>.Validation(refusal);
        }

        // Validate all sections BEFORE creating the app so registration is transactional. Validation
        // runs ONCE here, not in the adapters, so the two surfaces cannot validate differently -- the
        // same placement UpdateSettingsOperation uses. The adapter already assembled the divergent
        // input (REST's discovery->process merge, MCP's installDirectory injection) into
        // command.Overrides, so the operation validates every section uniformly.
        //
        // Reject any capability section the resolved app type does not bind. Before this,
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

        // The proxy routes by hostname, so a routed app's resolved hostname decides which requests
        // reach it. Registration is an Agent-level action, so the name must be well formed and not
        // already answered for -- by the dashboard or by another app -- or one registration could put
        // its route in front of someone else's traffic. Checked before anything is persisted.
        var hostname = ResolveHostname(appType.Slug, command.Slug, validatedOverrides);

        if (hostname is not null)
        {
            if (!CapabilityResolver.IsWellFormedHostname(hostname))
            {
                return OperationResult<CreateAppOutcome>.Validation
                (
                    $"routing.domainPattern: resolves to '{hostname}', which is not a valid hostname "
                    + "(each dot-separated label must be 1-63 characters and must not start or end with '-')."
                );
            }

            var claimant = await FindHostnameClaimantAsync(hostname, ct);

            if (claimant is not null)
            {
                return OperationResult<CreateAppOutcome>.Conflict
                (
                    $"routing.domainPattern: '{hostname}' is already {claimant}."
                );
            }
        }

        var app = new App
        {
            Slug = command.Slug,
            DisplayName = command.DisplayName,
            AppTypeSlug = appType.Slug
        };

        // Persist the App row and every capability override in ONE transaction (the store owns the
        // commit). The prior shape created the App, then looped saving overrides, each in its own
        // context -- so a failure mid-loop left a half-configured app on disk. The
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
        // apps skip that intermediate step -- the upstream is operator-declared -- so they
        // auto-enable at registration. Process-based apps have routes tied to process state and
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

        // External-route apps auto-enable at registration (above). Record app.started so the
        // activity feed reflects the route going live -- the same event the manual
        // start path records for routing-only apps. The 1-vs-2-event conditional: normal registration
        // records only app.created; an external-route records BOTH. Preserved exactly.
        if (hasExternalTarget)
        {
            await RecordAsync(ActivityEventTypes.AppStarted, app, ct);
        }

        // Surface a one-line `collabhost --update-hosts` hint for routed app types (an
        // internal-service registration has no proxy route and no hosts entry, so it stays silent).
        var hints = ResolveHelpfulNextSteps(hostname);

        return OperationResult<CreateAppOutcome>.Success(new CreateAppOutcome(app.Id, app.Slug, hints));
    }

    // A type marked internal exists for the apps Collabhost creates for itself: today only
    // system-service, the proxy's type, which the proxy seeder writes directly and never through this
    // operation. Such a type is already left out of every type list, so registration refuses it too,
    // and what an operator is offered and what registration accepts are one set. Apps of such a type
    // that already exist are untouched: editing, starting, stopping and deleting never consult the
    // flag. Public because register_app checks it before its own install-directory check, so a caller
    // is told the real reason.
    public static string? DescribeRefusal(AppType appType, TypeStore typeStore)
    {
        if (!appType.IsInternal)
        {
            return null;
        }

        var registrable = typeStore.ListTypes()
            .Where(type => !type.IsInternal)
                .Select(type => type.Slug);

        return $"App type '{appType.Slug}' cannot be registered: Collabhost uses it only for the apps it "
            + "creates for itself, such as its proxy. The types you can register are "
            + $"{string.Join(", ", registrable)}.";
    }

    private static IReadOnlyList<string> ResolveHelpfulNextSteps(string? hostname) =>
        hostname is null ? [] : [HostsHintBuilder.Compose(hostname)];

    // The hostname a routed app will answer on: the type's routing default merged with this
    // registration's routing override, resolved through the same CapabilityResolver path the proxy
    // uses, so an operator-set domain pattern is what gets checked and hinted. Null for a type with
    // no routing binding (no proxy route, so no hostname).
    private string? ResolveHostname
    (
        string appTypeSlug,
        string slug,
        IReadOnlyList<(string SectionKey, JsonObject Overrides)> validatedOverrides
    )
    {
        var bindings = _typeStore.GetBindings(appTypeSlug);

        if (bindings is null || !bindings.TryGetValue("routing", out var routingBindingJson))
        {
            return null;
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

        return CapabilityResolver.ResolveDomain(routing.DomainPattern, slug, _proxySettings.BaseDomain);
    }

    // Who already answers on this hostname, phrased for the conflict message, or null when nobody
    // does. Hostnames compare case-insensitively, as DNS and the proxy's host match do.
    private async Task<string?> FindHostnameClaimantAsync(string hostname, CancellationToken ct)
    {
        var dashboardHostname = $"{_portalSettings.Subdomain}.{_proxySettings.BaseDomain}";

        if (string.Equals(hostname, dashboardHostname, StringComparison.OrdinalIgnoreCase))
        {
            return "the dashboard's hostname";
        }

        foreach (var existing in await _store.ListAsync(ct))
        {
            if (!_typeStore.HasBinding(existing.AppTypeSlug, "routing"))
            {
                continue;
            }

            var routing = await _capabilityStore.ResolveAsync<RoutingConfiguration>("routing", existing, ct);

            if (routing is null)
            {
                continue;
            }

            var existingHostname = CapabilityResolver.ResolveDomain
            (
                routing.DomainPattern,
                existing.Slug,
                _proxySettings.BaseDomain
            );

            if (string.Equals(hostname, existingHostname, StringComparison.OrdinalIgnoreCase))
            {
                return $"used by app '{existing.Slug}'";
            }
        }

        return null;
    }
}
