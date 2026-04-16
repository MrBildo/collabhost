using System.ComponentModel;
using System.Globalization;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;
using Collabhost.Api.Supervisor;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Collabhost.Api.Mcp;

#pragma warning disable MA0076 // Ulid.ToString is not locale-sensitive
#pragma warning disable MA0011 // Ulid.ToString is not locale-sensitive
[McpServerToolType]
public class ConfigurationTools
(
    AppStore appStore,
    TypeStore typeStore,
    ProcessSupervisor supervisor,
    ProxyManager proxy,
    ProxySettings proxySettings,
    ICurrentUser currentUser,
    ActivityEventStore activityEventStore,
    ILogger<ConfigurationTools> logger
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

    private readonly ICurrentUser _currentUser = currentUser
        ?? throw new ArgumentNullException(nameof(currentUser));

    private readonly ActivityEventStore _activityEventStore = activityEventStore
        ?? throw new ArgumentNullException(nameof(activityEventStore));

    private readonly ILogger<ConfigurationTools> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

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
        CancellationToken ct
    )
    {
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

                var fields = new List<object>();

                foreach (var fieldDescriptor in definition.Schema)
                {
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
        CancellationToken ct
    )
    {
        var app = await _appStore.GetBySlugAsync(slug, ct);

        if (app is null)
        {
            return McpResponseFormatter.AppNotFound(slug);
        }

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

        var bindings = _typeStore.GetBindings(app.AppTypeSlug);
        var overrides = await _appStore.GetOverridesAsync(app.Id, ct);

        // Handle identity section changes
        if (changesObject.TryGetPropertyValue("identity", out var identityNode)
            && identityNode is JsonObject identityChanges
            && identityChanges.TryGetPropertyValue("displayName", out var displayNameNode))
        {
            var newDisplayName = displayNameNode?.GetValue<string>();

            if (!string.IsNullOrWhiteSpace(newDisplayName))
            {
                app.DisplayName = newDisplayName;
                app.ModifiedAt = DateTime.UtcNow;

                await _appStore.UpdateAppAsync(app, ct);
            }
        }

        // Handle capability section changes
        foreach (var (sectionKey, sectionValueNode) in changesObject)
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
                return McpResponseFormatter.InvalidParameters
                (
                    $"Unknown capability section '{sectionKey}'. Use get_settings to see valid sections for this app."
                );
            }

            var proposedOverrides = new JsonObject();

            foreach (var (fieldKey, fieldValue) in sectionChanges)
            {
                proposedOverrides[fieldKey] = fieldValue?.DeepClone();
            }

            var validationErrors = CapabilityResolver.ValidateEdits
            (
                sectionKey, proposedOverrides, isNewApp: false
            );

            if (validationErrors.Count > 0)
            {
                return McpResponseFormatter.InvalidParameters
                (
                    $"Validation errors for '{sectionKey}': {string.Join("; ", validationErrors)}"
                );
            }

            // Merge with existing override (only change provided fields)
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

            await _appStore.SaveOverrideAsync
            (
                app.Id,
                sectionKey,
                effectiveOverride.ToJsonString(McpResponseFormatter.JsonOptions),
                ct
            );
        }

        _appStore.Invalidate(slug);
        _appStore.InvalidateOverrides(app.Id);

        var changedCapabilities = changesObject.Select(kvp => kvp.Key)
            .Where(k => !string.Equals(k, "identity", StringComparison.Ordinal))
                .ToList();

        try
        {
            await _activityEventStore.RecordAsync
            (
                new ActivityEvent
                {
                    EventType = ActivityEventTypes.AppSettingsUpdated,
                    ActorId = _currentUser.UserId.ToString(),
                    ActorName = _currentUser.User.Name,
                    AppId = app.Id.ToString(),
                    AppSlug = app.Slug,
                    MetadataJson = JsonSerializer.Serialize(new { changedCapabilities })
                },
                ct
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record activity event for app.settings_updated (slug={Slug})", slug);
        }

        return McpResponseFormatter.Success
        (
            $"Settings updated for app '{slug}'. Use get_settings to review current values. "
            + "If any changed settings require restart, use restart_app to apply them."
        );
    }

    [McpServerTool
    (
        Name = "reload_proxy",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Forces Caddy to regenerate its proxy configuration from the current app registry state. Use this when routes appear stale or when an app's domain is not resolving correctly. This is safe to call at any time -- it regenerates the full configuration idempotently.")]
    public async Task<CallToolResult> ReloadProxyAsync(CancellationToken ct)
    {
        _proxy.RequestSync();

        try
        {
            await _activityEventStore.RecordAsync
            (
                new ActivityEvent
                {
                    EventType = ActivityEventTypes.ProxyReloaded,
                    ActorId = _currentUser.UserId.ToString(),
                    ActorName = _currentUser.User.Name,
                    AppId = null,
                    AppSlug = null,
                    MetadataJson = null
                },
                ct
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record activity event for proxy.reloaded");
        }

        return McpResponseFormatter.Success
        (
            "Proxy configuration reload requested. Caddy will regenerate its configuration from the current app registry state."
        );
    }

    [McpServerTool
    (
        Name = "list_routes",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Lists all proxy routes currently configured in Caddy. Each route shows the app slug, external domain ({slug}.<configured-base-domain>), upstream target, and whether the route is active. Use this to verify route configuration after starting or stopping an app.")]
    public async Task<CallToolResult> ListRoutesAsync(CancellationToken ct)
    {
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

            var target = routingConfiguration.ServeMode == ServeMode.ReverseProxy
                ? process?.Port is not null
                    ? string.Create(CultureInfo.InvariantCulture, $"localhost:{process.Port.Value}")
                    : "not-running"
                : "file-server";

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
            "routing" => 2,
            "health-check" => 3,
            "restart" => 4,
            "auto-start" => 5,
            "environment-defaults" => 6,
            "artifact" => 7,
            _ => 99
        };
    }
}
#pragma warning restore MA0011
#pragma warning restore MA0076
