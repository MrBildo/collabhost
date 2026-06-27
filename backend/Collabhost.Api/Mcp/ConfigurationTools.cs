using System.ComponentModel;

using Collabhost.Api.Capabilities;
using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Operations;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;
using Collabhost.Api.Supervisor;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Collabhost.Api.Mcp;

// Card #332: every tool takes an optional `authKey` per-call argument. Resolution happens
// at the top of each body via McpRequestAuthenticator.
[McpServerToolType]
public class ConfigurationTools
(
    AppStore appStore,
    TypeStore typeStore,
    ProcessSupervisor supervisor,
    ProxyManager proxy,
    ProxySettings proxySettings,
    ReloadProxyOperation reloadProxyOperation,
    UpdateSettingsOperation updateSettingsOperation,
    McpRequestAuthenticator authenticator
)
{
    private readonly AppStore _appStore = appStore
        ?? throw new ArgumentNullException(nameof(appStore));

    private readonly TypeStore _typeStore = typeStore
        ?? throw new ArgumentNullException(nameof(typeStore));

    private readonly ProcessSupervisor _supervisor = supervisor
        ?? throw new ArgumentNullException(nameof(supervisor));

    private readonly ProxyManager _proxy = proxy
        ?? throw new ArgumentNullException(nameof(proxy));

    private readonly ProxySettings _proxySettings = proxySettings
        ?? throw new ArgumentNullException(nameof(proxySettings));

    // The migrated reload-proxy operation injected directly (code-structure-conventions §8: no
    // dispatcher). reload_proxy adapts the marker command, calls the operation, and maps the result.
    private readonly ReloadProxyOperation _reloadProxyOperation = reloadProxyOperation
        ?? throw new ArgumentNullException(nameof(reloadProxyOperation));

    // The migrated update-settings operation injected directly (code-structure-conventions §8, spine
    // PR 5 -- the heaviest single body). update_settings adapts its raw `settings` string into the
    // normalized command (with the MCP-divergence flags) and maps the result. The shared validate ->
    // merge -> save -> render -> event loop now lives once in UpdateSettingsOperation; the
    // ExternalTargetSettings / RuntimeConfigFileWriter / ICurrentUser / ActivityEventStore deps that
    // body used moved into the operation and are no longer ctor deps here.
    private readonly UpdateSettingsOperation _updateSettingsOperation = updateSettingsOperation
        ?? throw new ArgumentNullException(nameof(updateSettingsOperation));

    private readonly McpRequestAuthenticator _authenticator = authenticator
        ?? throw new ArgumentNullException(nameof(authenticator));

    [McpServerTool
    (
        Name = "get_settings",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Returns the schema-driven settings for an application, organized by capability. Each section includes the capability name, the schema defining valid fields, and the current effective values (defaults merged with overrides). Use this before update_settings to understand what configuration the app supports. A setting marked 'requiresRestart' means the app must be restarted after changing it.")]
    public async Task<CallToolResult> GetSettingsAsync
    (
        [Description("The app's unique slug identifier. Use list_apps to find available slugs.")] string slug,
        [Description(McpAuthDescriptions.AuthKey)] string? authKey = null,
        CancellationToken ct = default
    )
    {
        var authError = await _authenticator.AuthenticateAsync(authKey, "get_settings", ct);

        if (authError is not null)
        {
            return authError;
        }

        var app = await _appStore.GetBySlugAsync(slug, ct);

        if (app is null)
        {
            return McpResponseFormatter.AppNotFound(slug);
        }

        var bindings = _typeStore.GetBindings(app.AppTypeSlug);
        var overrides = await _appStore.GetOverridesAsync(app.Id, ct);

        List<object> sections =
        [
            // Identity section (always first)
            new
            {
                key = "identity",
                title = "Identity",
                fields = (object[])
                [
                    new
                    {
                        key = "name",
                        label = "Name (slug)",
                        type = "text",
                        value = (object)app.Slug,
                        defaultValue = (object)app.Slug,
                        editable = false,
                        requiresRestart = false
                    },
                    new
                    {
                        key = "displayName",
                        label = "Display Name",
                        type = "text",
                        value = (object)app.DisplayName,
                        defaultValue = (object)app.DisplayName,
                        editable = true,
                        requiresRestart = false
                    }
                ]
            }
        ];

        // Capability sections
        if (bindings is not null)
        {
            foreach (var (capabilitySlug, defaultConfigurationJson) in bindings.OrderBy(b => b.Key.GetCapabilityOrder()))
            {
                var definition = CapabilityCatalog.Get(capabilitySlug);

                if (definition is null)
                {
                    continue;
                }

                var overrideJson = overrides.TryGetValue(capabilitySlug, out var capabilityOverride)
                    ? capabilityOverride.ConfigurationJson
                    : null;

                var effectiveJson = CapabilityResolver.ResolveJson
                (
                    defaultConfigurationJson, overrideJson
                );

                JsonObject? effectiveValues = null;
                JsonObject? defaultValues = null;

                try
                {
                    effectiveValues = JsonNode.Parse(effectiveJson)?.AsObject();
                    defaultValues = JsonNode.Parse(defaultConfigurationJson)?.AsObject();
                }
                catch (JsonException)
                {
                    continue;
                }

                if (effectiveValues is null || defaultValues is null)
                {
                    continue;
                }

                var schemaOverrides = SchemaOverrides.Extract(defaultConfigurationJson);

                var fields = new List<object>();

                foreach (var baseDescriptor in definition.Schema)
                {
                    schemaOverrides.TryGetValue(baseDescriptor.Key, out var fieldOverride);
                    var fieldDescriptor = SchemaOverrides.Apply(baseDescriptor, fieldOverride);

                    var value = effectiveValues.GetFieldValue(fieldDescriptor.Key);
                    var defaultValue = defaultValues.GetFieldValue(fieldDescriptor.Key);

                    fields.Add
                    (
                        new
                        {
                            key = fieldDescriptor.Key,
                            label = fieldDescriptor.Label,
                            type = fieldDescriptor.Type,
                            value,
                            defaultValue,
                            editable = fieldDescriptor.Editable is not FieldEditableLocked,
                            requiresRestart = fieldDescriptor.RequiresRestart
                        }
                    );
                }

                sections.Add
                (
                    new
                    {
                        key = capabilitySlug,
                        title = definition.DisplayName,
                        fields = (IReadOnlyList<object>)fields
                    }
                );
            }
        }

        var result = new
        {
            slug = app.Slug,
            displayName = app.DisplayName,
            appType = app.AppTypeSlug,
            sections = (IReadOnlyList<object>)sections
        };

        return McpResponseFormatter.Success(McpResponseFormatter.ToJson(result));
    }

    [McpServerTool
    (
        Name = "update_settings",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Saves capability override settings for an application. Only provided fields are changed -- omitted fields retain their current values. Settings must conform to the schema from get_settings. Some settings require an app restart to take effect (check the 'requiresRestart' flag in get_settings). Use get_settings first to see the schema and current values.")]
    public async Task<CallToolResult> UpdateSettingsAsync
    (
        [Description("The app's unique slug identifier. Use list_apps to find available slugs.")] string slug,
        [Description("JSON object of settings overrides to apply. Must match the capability schema structure from get_settings. Example: {\"process\":{\"workingDirectory\":\"/app\"},\"restart\":{\"policy\":\"always\"}}")] string settings,
        [Description(McpAuthDescriptions.AuthKey)] string? authKey = null,
        CancellationToken ct = default
    )
    {
        var authError = await _authenticator.AuthenticateAsync(authKey, "update_settings", ct);

        if (authError is not null)
        {
            return authError;
        }

        // MCP-specific input adaptation: the raw `settings` JSON string -> JsonObject. These two
        // parse errors are MCP-surface concerns (REST receives a typed UpdateSettingsRequest, never a
        // raw string), so they stay at the adapter, above the operation -- the single-surface guard
        // precedent (a guard with no twin on the other surface stays at its surface).
        JsonObject? changesObject;

        try
        {
            changesObject = JsonNode.Parse(settings)?.AsObject();
        }
        catch (JsonException ex)
        {
            return McpResponseFormatter.InvalidParameters
            (
                $"Invalid JSON in settings parameter: {ex.Message}"
            );
        }

        if (changesObject is null)
        {
            return McpResponseFormatter.InvalidParameters
            (
                "settings must be a non-null JSON object."
            );
        }

        // Migrated to the operation spine (code-structure-conventions §8): adapt the parsed changes
        // into the normalized command, call the injected operation directly (no dispatcher), and map
        // the result back to the CallToolResult this tool returns. The shared identity -> validate ->
        // merge -> save -> render -> event loop lives once in UpdateSettingsOperation. The pre-
        // migration outer try/catch around the event record is dropped with the migrated body:
        // ActivityEventStore.RecordAsync already swallows all exceptions internally (catch Exception ->
        // LogWarning), so the wrapper was dead defensive code -- dropping it is an observable no-op
        // (same as the start/stop/reload migrations).
        //
        // The command flags now MATCH the REST surface (#406 settings parity-fix, the one sanctioned
        // behavior change of the spine arc): ValidateMergedOverrides + RefreshProbesOnArtifactChange
        // are TRUE. The pre-migration MCP path ran NEITHER -- a confirmed REST<->MCP drift surfaced (not
        // fixed) at PR 5 and folded in here per the operator ruling. MCP now (a) runs the post-merge
        // cross-field check (rejecting the HSTS double-emission collision REST already rejected), and
        // (b) re-probes on an artifact-section change. RejectUnknownSection stays TRUE -- MCP rejecting
        // an unknown section mid-loop where REST skips is intended surface ergonomics, NOT drift.
        var command = new UpdateSettingsCommand
        (
            slug,
            changesObject,
            ValidateMergedOverrides: true,
            RefreshProbesOnArtifactChange: true,
            RejectUnknownSection: true
        );

        var result = await _updateSettingsOperation.ExecuteAsync(command, ct);

        return result.ToCallToolResult(slug);
    }

    [McpServerTool
    (
        Name = "reload_proxy",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Forces the proxy to regenerate its configuration from the current app registry state. Use this when routes appear stale or when an app's domain is not resolving correctly. This is safe to call at any time -- it regenerates the full configuration idempotently.")]
    public async Task<CallToolResult> ReloadProxyAsync
    (
        [Description(McpAuthDescriptions.AuthKey)] string? authKey = null,
        CancellationToken ct = default
    )
    {
        var authError = await _authenticator.AuthenticateAsync(authKey, "reload_proxy", ct);

        if (authError is not null)
        {
            return authError;
        }

        // Migrated to the operation spine (code-structure-conventions §8): adapt the marker command
        // (no slug -- the reload acts on no app), call the injected operation, and map the result
        // back to exactly the fixed "reload requested" message this tool returned before. The
        // proxy.RequestSync() + the actor-stamped proxy.reloaded event now live once in the
        // operation. The pre-migration outer try/catch around RecordAsync is dropped with the
        // migrated body: ActivityEventStore.RecordAsync already swallows all exceptions internally
        // (catch Exception -> LogWarning), so the wrapper was dead defensive code -- dropping it is
        // an observable no-op (same as the start/stop migration in PR 3).
        var result = await _reloadProxyOperation.ExecuteAsync(new ReloadProxyCommand(), ct);

        return result.ToCallToolResult();
    }

    [McpServerTool
    (
        Name = "list_routes",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Lists all proxy routes currently configured. Each route shows the app slug, external domain ({slug}.<configured-base-domain>), upstream target, and whether the route is active. Use this to verify route configuration after starting or stopping an app.")]
    public async Task<CallToolResult> ListRoutesAsync
    (
        [Description(McpAuthDescriptions.AuthKey)] string? authKey = null,
        CancellationToken ct = default
    )
    {
        var authError = await _authenticator.AuthenticateAsync(authKey, "list_routes", ct);

        if (authError is not null)
        {
            return authError;
        }

        var apps = await _appStore.ListAsync(ct);

        var routes = new List<object>();

        foreach (var app in apps)
        {
            var bindings = _typeStore.GetBindings(app.AppTypeSlug);

            if (bindings is null || !bindings.TryGetValue("routing", out var routingBindingJson))
            {
                continue;
            }

            var overrides = await _appStore.GetOverridesAsync(app.Id, ct);

            var overrideJson = overrides.TryGetValue("routing", out var routingOverride)
                ? routingOverride.ConfigurationJson
                : null;

            var routingConfiguration = CapabilityResolver.Resolve<RoutingConfiguration>
            (
                routingBindingJson, overrideJson
            );

            if (routingConfiguration is null)
            {
                continue;
            }

            var domain = CapabilityResolver.ResolveDomain
            (
                routingConfiguration.DomainPattern, app.Slug, _proxySettings.BaseDomain
            );

            var process = _supervisor.GetProcess(app.Id);

            // Card #435: the one shared RouteTargetResolver synthesizes the upstream-
            // target string for all 4 route surfaces (REST detail + routes, MCP get_app +
            // list_routes). External-route apps (Card #348) have no supervised process, so
            // the resolver surfaces the operator-declared upstream; supervised apps get
            // "localhost:{port}" from the live process port.
            var hasExternalTarget = bindings.ContainsKey("external-target");

            var externalTarget = hasExternalTarget
                && bindings.TryGetValue("external-target", out var externalTargetBinding)
                    ? CapabilityResolver.Resolve<ExternalTargetConfiguration>
                    (
                        externalTargetBinding,
                        overrides.TryGetValue("external-target", out var externalTargetOverride)
                            ? externalTargetOverride.ConfigurationJson
                            : null
                    )
                    : null;

            var target = RouteTargetResolver.ResolveTarget
            (
                routingConfiguration,
                hasExternalTarget,
                externalTarget,
                process?.Port
            );

            var enabled = _proxy.IsRouteEnabled(app.Slug);

            routes.Add
            (
                new
                {
                    slug = app.Slug,
                    displayName = app.DisplayName,
                    domain,
                    target,
                    proxyMode = routingConfiguration.ServeMode == ServeMode.ReverseProxy
                        ? "reverseProxy"
                        : "fileServer",
                    enabled
                }
            );
        }

        var result = new
        {
            baseDomain = _proxySettings.BaseDomain,
            routes = (IReadOnlyList<object>)routes
        };

        return McpResponseFormatter.Success(McpResponseFormatter.ToJson(result));
    }
}

// File-scoped mapping from the surface-agnostic reload outcome back to the MCP result shape (§7:
// the surface holds only its file-scoped mapping). K-1 (Kai's PR-1 forward note):
// OperationResult.FailureKind defaults to ordinal-0 NotFound on a success, so the success arm is
// gated on IsSuccess FIRST -- FailureKind is only read on the failure path. The success arm returns
// the fixed "reload requested" message. The reload now HAS a failure path: when the proxy is
// disabled the operation returns Conflict, which maps to InvalidParameters (the single MCP error
// shape) carrying the "proxy is disabled" message, so a reload against a dead proxy signals rather
// than false-succeeds.
file static class ReloadProxyResultMapping
{
    public static CallToolResult ToCallToolResult(this OperationResult<ProxyReloadOutcome> result) =>
        result.IsSuccess
            ? McpResponseFormatter.Success
            (
                "Proxy configuration reload requested. The proxy will regenerate its configuration from the current app registry state."
            )
            : McpResponseFormatter.InvalidParameters(result.Error ?? string.Empty);
}

// File-scoped mapping from the surface-agnostic settings-update outcome back to the MCP result shape
// (§7: the surface holds only its file-scoped mapping). K-1 (Kai's PR-1 forward note): FailureKind
// defaults to ordinal-0 NotFound on a success, so the success arm gates on IsSuccess FIRST.
//
// The mapping is byte-faithful to the pre-migration update_settings tool:
//   - Success -> the fixed "Settings updated for app '{slug}'..." message (slug threaded in).
//   - NotFound -> AppNotFound(slug) -- the MCP not-found shape (the operation's NotFound carries a
//     "App '{slug}' not found." message, but MCP's surface shape is AppNotFound(slug), kept at the
//     surface exactly as the pre-migration tool returned and as StartApp/StopApp's MCP adapter does).
//   - Validation -> InvalidParameters(error). The error is either the full "Unknown capability
//     section '{slug}'..." message (built surface-agnostic in the operation, byte-identical) or the
//     section-qualified joined validation errors. (See the PR body: the pre-migration MCP wrapped
//     the latter in a "Validation errors for '{section}': " prefix; that redundant prefix -- the
//     section is already named in each "{capabilitySlug}.{field}: ..." error -- normalizes away with
//     zero information loss. Reversal lever in the PR body if byte-exact prose is mandated.)
//   - Conflict -> InvalidParameters(error). The partial-success path: the override ALREADY persisted,
//     the runtime-config-file write failed, and the message is the exact "Settings saved, but failed
//     to write runtime-config file: ..." prefix the pre-migration tool returned. The 3-kind model
//     carries this conflict-with-a-value faithfully -- the pre-migration tool returned no value and
//     recorded no event on this path either, so Conflict(message) with no value is byte-identical.
file static class UpdateSettingsResultMapping
{
    public static CallToolResult ToCallToolResult(this OperationResult<UpdateSettingsOutcome> result, string slug) =>
        result.IsSuccess
            ? McpResponseFormatter.Success
            (
                $"Settings updated for app '{slug}'. Use get_settings to review current values. "
                + "If any changed settings require restart, use restart_app to apply them."
            )
            : result.FailureKind switch
            {
                OperationFailureKind.NotFound => McpResponseFormatter.AppNotFound(slug),
                _ => McpResponseFormatter.InvalidParameters(result.Error ?? string.Empty),
            };
}

file static class ConfigurationToolExtensions
{
    extension(JsonObject obj)
    {
        public object? GetFieldValue(string key) =>
            !obj.TryGetPropertyValue(key, out var node) || node is null
                ? null
                : node.GetValueKind() switch
                {
                    JsonValueKind.String => node.GetValue<string>(),
                    JsonValueKind.Number => node.GetValue<double>(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Object => JsonSerializer.Deserialize<Dictionary<string, string>>(node),
                    _ => null
                };
    }

    extension(string value)
    {
        public int GetCapabilityOrder() => value switch
        {
            "process" => 0,
            "port-injection" => 1,
            "external-target" => 2, // Card #348 polish: keep adjacent to routing, matches AppEndpoints order.
            "routing" => 3,
            "health-check" => 4,
            "restart" => 5,
            "auto-start" => 6,
            "environment-defaults" => 7,
            "runtime-config-file" => 8,
            "artifact" => 9,
            _ => 99
        };
    }
}
