using System.ComponentModel;
using System.Globalization;

using Collabhost.Api.Capabilities;
using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;
using Collabhost.Api.Supervisor;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Collabhost.Api.Mcp;

[McpServerToolType]
public class ConfigurationTools
(
    AppStore appStore,
    ProcessSupervisor supervisor,
    ProxyManager proxy,
    ProxySettings proxySettings
)
{
    private readonly AppStore _appStore = appStore
        ?? throw new ArgumentNullException(nameof(appStore));

    private readonly ProcessSupervisor _supervisor = supervisor
        ?? throw new ArgumentNullException(nameof(supervisor));

    private readonly ProxyManager _proxy = proxy
        ?? throw new ArgumentNullException(nameof(proxy));

    private readonly ProxySettings _proxySettings = proxySettings
        ?? throw new ArgumentNullException(nameof(proxySettings));

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

        var bindings = await _appStore.GetBindingsAsync(app.AppTypeId, ct);
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
        foreach (var binding in bindings.OrderBy(b => b.CapabilitySlug.GetCapabilityOrder()))
        {
            var definition = CapabilityCatalog.Get(binding.CapabilitySlug);

            if (definition is null)
            {
                continue;
            }

            var overrideJson = overrides.TryGetValue(binding.CapabilitySlug, out var capabilityOverride)
                ? capabilityOverride.ConfigurationJson
                : null;

            var effectiveJson = CapabilityResolver.ResolveJson
            (
                binding.DefaultConfigurationJson, overrideJson
            );

            JsonObject? effectiveValues = null;
            JsonObject? defaultValues = null;

            try
            {
                effectiveValues = JsonNode.Parse(effectiveJson)?.AsObject();
                defaultValues = JsonNode.Parse(binding.DefaultConfigurationJson)?.AsObject();
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
                    key = binding.CapabilitySlug,
                    title = definition.DisplayName,
                    fields = (IReadOnlyList<object>)fields
                }
            );
        }

        var result = new
        {
            slug = app.Slug,
            displayName = app.DisplayName,
            appType = app.AppType.Slug,
            sections = (IReadOnlyList<object>)sections
        };

        return McpResponseFormatter.Success(McpResponseFormatter.ToJson(result));
    }

    [McpServerTool
    (
        Name = "list_routes",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Lists all proxy routes currently configured in Caddy. Each route shows the app slug, external domain ({slug}.collab.internal), upstream target, and whether the route is active. Use this to verify route configuration after starting or stopping an app.")]
    public async Task<CallToolResult> ListRoutesAsync(CancellationToken ct)
    {
        var apps = await _appStore.ListAsync(ct);

        var routes = new List<object>();

        foreach (var app in apps)
        {
            var bindings = await _appStore.GetBindingsAsync(app.AppTypeId, ct);

            var routingBinding = bindings.SingleOrDefault
            (
                b => string.Equals(b.CapabilitySlug, "routing", StringComparison.Ordinal)
            );

            if (routingBinding is null)
            {
                continue;
            }

            var overrides = await _appStore.GetOverridesAsync(app.Id, ct);

            var overrideJson = overrides.TryGetValue("routing", out var routingOverride)
                ? routingOverride.ConfigurationJson
                : null;

            var routingConfiguration = CapabilityResolver.Resolve<RoutingConfiguration>
            (
                routingBinding.DefaultConfigurationJson, overrideJson
            );

            if (routingConfiguration is null)
            {
                continue;
            }

            var domain = routingConfiguration.DomainPattern
                .Replace("{slug}", app.Slug, StringComparison.OrdinalIgnoreCase);

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
