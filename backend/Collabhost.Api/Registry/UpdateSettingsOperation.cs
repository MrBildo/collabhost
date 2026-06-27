using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Operations;
using Collabhost.Api.Probes;
using Collabhost.Api.Proxy;
using Collabhost.Api.Shared;
using Collabhost.Api.StaticSite;

namespace Collabhost.Api.Registry;

// Update an app's settings (code-structure-conventions §8/§9 -- a concrete operation in its owning
// subsystem). The heaviest single operation in the arc: the shared core is identity change -> per-
// section [validate-edits -> merge-with-existing -> save] -> invalidate caches -> conditional
// runtime-config-file render -> record event. Both surfaces (REST SaveAppSettingsAsync, MCP
// update_settings) re-implemented this body near-line-for-line and hand-synced it across cards
// #350/#365/#366 -- the exact REST<->MCP drift §8 deletes.
//
// The body is intent only: load -> identity -> per-section validate/merge/save -> invalidate ->
// (probe-refresh) -> (render) -> record -> shape the outcome. No try/catch-to-result (the base
// hoists InvalidOperationException -> Conflict), no hand-built ActivityEvent (the base RecordAsync
// helper stamps the actor + carries the metadata). The one operation-specific failure the leaf
// translates itself is the runtime-config-file writer's RuntimeConfigFileWriteException -- a
// settings-render concern only this operation (and StartAppOperation) has, so it stays in the leaf
// (not hoisted into the base, which would burden every other operation) and maps to Conflict with
// the exact "Settings saved, but failed to write..." prefix BOTH surfaces returned. This is the
// arc's partial-success conflict-with-value case: the override IS already persisted when the render
// fails, so the failure is a genuine "partly succeeded" -- represented faithfully in the 3-kind
// model as Conflict(message) with no value. The pre-migration code returned NO value on this path
// either (REST returned a 409 Problem, never the fresh AppSettings) and recorded NO event, so the
// fact that the settings persisted is communicated exactly as before: through the message prefix.
//
// The three command flags carry the per-surface divergence (see _OperationContracts.cs):
// ValidateMergedOverrides + RefreshProbesOnArtifactChange are now BOTH-true on both surfaces (the
// #406 settings parity-fix closed the former REST-true / MCP-false drift); RejectUnknownSection is
// REST-false (skip) / MCP-true (mid-loop reject) intended divergence.
public sealed class UpdateSettingsOperation
(
    AppStore store,
    TypeStore typeStore,
    ProbeService probeService,
    ProxyManager proxy,
    RuntimeConfigFileWriter runtimeConfigFileWriter,
    ExternalTargetSettings externalTargetSettings,
    ICurrentUser currentUser,
    ActivityEventStore activityEventStore
) : Operation<UpdateSettingsCommand, UpdateSettingsOutcome>(currentUser, activityEventStore)
{
    // The override-serialization options. Value-equivalent for a JsonObject to both surfaces'
    // pre-migration options (REST's local _jsonOptions and MCP's McpResponseFormatter.JsonOptions):
    // JsonNode.ToJsonString ignores DefaultIgnoreCondition (it applies to POCO property
    // serialization, not to a JsonNode tree) and PropertyNameCaseInsensitive (a deserialization
    // concern), so the only behaviorally-relevant option -- the CamelCase naming policy + enum
    // converter -- matches both. The stored JSON is byte-identical to either pre-migration path.
    private static readonly JsonSerializerOptions _overrideSerializationOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly AppStore _store = store
        ?? throw new ArgumentNullException(nameof(store));

    private readonly TypeStore _typeStore = typeStore
        ?? throw new ArgumentNullException(nameof(typeStore));

    private readonly ProbeService _probeService = probeService
        ?? throw new ArgumentNullException(nameof(probeService));

    private readonly ProxyManager _proxy = proxy
        ?? throw new ArgumentNullException(nameof(proxy));

    private readonly RuntimeConfigFileWriter _runtimeConfigFileWriter = runtimeConfigFileWriter
        ?? throw new ArgumentNullException(nameof(runtimeConfigFileWriter));

    // Card #348, D3. Threaded into CapabilityResolver.ValidateEdits when the section under edit is
    // external-target so the host-pattern check honors the operator's public-hosts opt-in.
    private readonly ExternalTargetSettings _externalTargetSettings = externalTargetSettings
        ?? throw new ArgumentNullException(nameof(externalTargetSettings));

    protected override async Task<OperationResult<UpdateSettingsOutcome>> ExecuteCoreAsync
    (
        UpdateSettingsCommand command,
        CancellationToken ct
    )
    {
        var app = await _store.GetBySlugAsync(command.Slug, ct);

        if (app is null)
        {
            return OperationResult<UpdateSettingsOutcome>.NotFound($"App '{command.Slug}' not found.");
        }

        var bindings = _typeStore.GetBindings(app.AppTypeSlug);
        var overrides = await _store.GetOverridesAsync(app.Id, ct);

        // Handle identity section changes (display name only -- the slug is immutable).
        if (command.Changes.TryGetPropertyValue("identity", out var identityNode)
            && identityNode is JsonObject identityChanges
            && identityChanges.TryGetPropertyValue("displayName", out var displayNameNode))
        {
            var newDisplayName = displayNameNode?.GetValue<string>();

            if (!string.IsNullOrWhiteSpace(newDisplayName))
            {
                app.DisplayName = newDisplayName;
                app.ModifiedAt = DateTime.UtcNow;

                await _store.UpdateAppAsync(app, ct);
            }
        }

        // Handle capability section changes, one section at a time in document order: validate the
        // edits, merge them with the existing override, then save. An unknown section is skipped
        // (REST) or rejected mid-loop (MCP) per the command flag; the cross-field merged-validation
        // runs only when the command asks for it (REST).
        foreach (var (sectionKey, sectionValueNode) in command.Changes)
        {
            if (string.Equals(sectionKey, "identity", StringComparison.Ordinal))
            {
                continue;
            }

            if (sectionValueNode is not JsonObject sectionChanges)
            {
                continue;
            }

            if (bindings is null || !bindings.ContainsKey(sectionKey))
            {
                if (command.RejectUnknownSection)
                {
                    // The unknown-section reject (MCP-only: REST passes RejectUnknownSection false and
                    // skips). The message is operator-prose, not REST<->MCP-divergent (REST never
                    // produces this path), so it is surface-agnostic and passes through the MCP
                    // adapter verbatim -- byte-identical to the pre-migration MCP message. The reject
                    // is mid-loop (preceding known sections in document order are already saved),
                    // preserving the pre-migration order exactly.
                    return OperationResult<UpdateSettingsOutcome>.Validation
                    (
                        $"Unknown capability section '{sectionKey}'. Use get_settings to see valid sections for this app."
                    );
                }

                continue;
            }

            var proposedOverrides = new JsonObject();

            foreach (var (fieldKey, fieldValue) in sectionChanges)
            {
                proposedOverrides[fieldKey] = fieldValue?.DeepClone();
            }

            var validationErrors = CapabilityResolver.ValidateEdits
            (
                sectionKey, proposedOverrides, false, _externalTargetSettings.AllowPublicHosts
            );

            if (validationErrors.Count > 0)
            {
                // Surface-agnostic: the section-qualified joined errors (ValidateEdits already
                // prefixes each error with its capability slug and field key). REST returns this
                // verbatim with a 400; the MCP adapter wraps it in its own validation prose.
                return OperationResult<UpdateSettingsOutcome>.Validation(string.Join("; ", validationErrors));
            }

            // Merge with the existing override -- only the provided fields change.
            var existingOverrideJson = overrides.TryGetValue(sectionKey, out var existing)
                ? existing.ConfigurationJson
                : null;

            var effectiveOverride = existingOverrideJson is not null
                ? JsonNode.Parse(existingOverrideJson)?.AsObject() ?? []
                : (JsonObject)[];

            foreach (var (fieldKey, fieldValue) in sectionChanges)
            {
                effectiveOverride[fieldKey] = fieldValue?.DeepClone();
            }

            // Cross-field validation on the post-merge effective override (the #365-era two-step
            // operator path: e.g. save STS in headers first, later toggle EnableHsts -- neither delta
            // alone trips the in-flight ValidateEdits, the merged state does). Both surfaces run it
            // (the #406 settings parity-fix flipped the MCP flag true; see _OperationContracts.cs).
            if (command.ValidateMergedOverrides)
            {
                var mergedValidationErrors = CapabilityResolver.ValidateMergedOverrides
                (
                    sectionKey, effectiveOverride
                );

                if (mergedValidationErrors.Count > 0)
                {
                    return OperationResult<UpdateSettingsOutcome>.Validation(string.Join("; ", mergedValidationErrors));
                }
            }

            await _store.SaveOverrideAsync
            (
                app.Id,
                sectionKey,
                effectiveOverride.ToJsonString(_overrideSerializationOptions),
                ct
            );
        }

        _store.Invalidate(app.Slug);
        _store.InvalidateOverrides(app.Id);

        // Re-probe when artifact config changes (location or project root). Both surfaces do this
        // (the #406 settings parity-fix flipped the MCP flag true; the pre-migration MCP path never
        // re-probed -- the confirmed drift this fix closed).
        if (command.RefreshProbesOnArtifactChange && command.Changes.ContainsKey("artifact"))
        {
            _probeService.InvalidateProbeCache(app.Id);

            await _probeService.RunProbesAsync(app.Id, ct);
        }

        // Re-render the runtime-config file when its capability values change and the route is
        // currently enabled. The route-enable path handles the start-time write; this handles edits
        // while the route is already live. A write FAILURE is the arc's partial-success case: the
        // override above ALREADY persisted, so the failure is a genuine "partly succeeded" -- mapped
        // to Conflict with the exact prefix both surfaces returned, carrying NO outcome value (the
        // pre-migration code returned no value and recorded no event on this path either). The gate
        // uses IsRouteEnabled (default-true when _routeStates has no entry), the post-#365 semantic --
        // see RuntimeConfigFileWriter / Card #365 for the full rationale.
        if (command.Changes.ContainsKey("runtime-config-file") && _proxy.IsRouteEnabled(app.Slug))
        {
            var renderError = await RenderRuntimeConfigFileAsync(app, ct);

            if (renderError is not null)
            {
                return OperationResult<UpdateSettingsOutcome>.Conflict
                (
                    "Settings saved, but failed to write runtime-config file: " + renderError
                );
            }
        }

        var changedCapabilities = command.Changes.Select(kvp => kvp.Key)
            .Where(k => !string.Equals(k, "identity", StringComparison.Ordinal))
                .ToList();

        await RecordAsync
        (
            ActivityEventTypes.AppSettingsUpdated,
            app.Id.ToCanonicalString(),
            app.Slug,
            JsonSerializer.Serialize(new { changedCapabilities }),
            ct
        );

        return OperationResult<UpdateSettingsOutcome>.Success(new UpdateSettingsOutcome(app.Slug));
    }

    // The runtime-config-file render, returning the operator-actionable error message on failure (or
    // null on success). RuntimeConfigFileWriteException is the partial-success failure only this
    // operation (and StartAppOperation) translates -- the leaf owns its translation to a normalized
    // OperationResult.Conflict above, kept out of the base (which would force the same catch on every
    // other operation that never throws it).
    private async Task<string?> RenderRuntimeConfigFileAsync(App app, CancellationToken ct)
    {
        try
        {
            await _runtimeConfigFileWriter.RenderAsync(app, ct);

            return null;
        }
        catch (RuntimeConfigFileWriteException exception)
        {
            return exception.Message;
        }
    }
}
