using System.ComponentModel;
using System.Globalization;
using System.Reflection;

using Collabhost.Api.Capabilities;
using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Probes;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;
using Collabhost.Api.Supervisor;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Collabhost.Api.Mcp;

[McpServerToolType]
public class DiscoveryTools
(
    AppStore appStore,
    ProcessSupervisor supervisor,
    ProxyManager proxy,
    ProbeService probeService
)
{
    private readonly AppStore _appStore = appStore
        ?? throw new ArgumentNullException(nameof(appStore));

    private readonly ProcessSupervisor _supervisor = supervisor
        ?? throw new ArgumentNullException(nameof(supervisor));

    private readonly ProxyManager _proxy = proxy
        ?? throw new ArgumentNullException(nameof(proxy));

    private readonly ProbeService _probeService = probeService
        ?? throw new ArgumentNullException(nameof(probeService));

    private static readonly DateTime _startedAt = DateTime.UtcNow;

    [McpServerTool
    (
        Name = "get_system_status",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Returns system hostname, Collabhost version, and uptime. Use this to identify the host and verify the platform version. This tool does not require any apps to be registered.")]
    public static CallToolResult GetSystemStatus()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";

        var uptimeSeconds = Math.Round((DateTime.UtcNow - _startedAt).TotalSeconds, 1);

        var uptimeFormatted = FormatUptime(uptimeSeconds);

        var result = new
        {
            hostname = Environment.MachineName,
            version,
            uptimeSeconds,
            uptimeFormatted
        };

        return McpResponseFormatter.Success(McpResponseFormatter.ToJson(result));
    }

    [McpServerTool
    (
        Name = "list_apps",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Lists all registered applications with their current status, type, and route URL. Use this to discover app slugs before calling other app-specific tools. The 'status' filter narrows results to apps in a specific state.")]
    public async Task<CallToolResult> ListAppsAsync
    (
        [Description("Filter by app status. Valid values: running, stopped, crashed, backoff, fatal. If omitted, returns all apps.")] string? status,
        CancellationToken ct
    )
    {
        var apps = await _appStore.ListAsync(ct);

        var items = new List<object>();

        foreach (var app in apps)
        {
            var process = _supervisor.GetProcess(app.Id);
            var bindings = await _appStore.GetBindingsAsync(app.AppTypeId, ct);
            var overrides = await _appStore.GetOverridesAsync(app.Id, ct);

            var hasProcess = bindings.Any
            (
                b => string.Equals(b.CapabilitySlug, "process", StringComparison.Ordinal)
            );

            var hasRouting = bindings.Any
            (
                b => string.Equals(b.CapabilitySlug, "routing", StringComparison.Ordinal)
            );

            var routingBinding = bindings.SingleOrDefault
            (
                b => string.Equals(b.CapabilitySlug, "routing", StringComparison.Ordinal)
            );

            RoutingConfiguration? routingConfiguration = null;

            if (routingBinding is not null)
            {
                var overrideJson = overrides.TryGetValue("routing", out var routingOverride)
                    ? routingOverride.ConfigurationJson
                    : null;

                routingConfiguration = CapabilityResolver.Resolve<RoutingConfiguration>
                (
                    routingBinding.DefaultConfigurationJson, overrideJson
                );
            }

            var domain = routingConfiguration?.DomainPattern
                .Replace("{slug}", app.Slug, StringComparison.OrdinalIgnoreCase);

            var routeEnabled = routingConfiguration is not null && _proxy.IsRouteEnabled(app.Slug);

            var resolvedStatus = ResolveStatus(hasProcess, process, hasRouting, routeEnabled);
            var statusString = resolvedStatus.ToApiString();

            if (status is not null && !string.Equals(statusString, status, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            items.Add
            (
                new
                {
                    slug = app.Slug,
                    displayName = app.DisplayName,
                    status = statusString,
                    appType = app.AppType.Slug,
                    domain,
                    port = process?.Port,
                    pid = process?.Pid,
                    uptimeSeconds = process?.UptimeSeconds
                }
            );
        }

        var header = "Use slug to identify apps in other tool calls.";
        var json = McpResponseFormatter.ToJson(items);

        return McpResponseFormatter.Success($"{header}\n{json}");
    }

    [McpServerTool
    (
        Name = "get_app",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Returns detailed information about a specific application including identity, status, process info, route URL, capabilities, and technology probe data (detected runtime, frameworks, dependencies). Use this to inspect an app's current state before taking action. Probe data is cached and refreshed on app start and config changes.")]
    public async Task<CallToolResult> GetAppAsync
    (
        [Description("The app's unique slug identifier (e.g., 'my-api-server'). Use list_apps to find available slugs.")] string slug,
        CancellationToken ct
    )
    {
        var app = await _appStore.GetBySlugAsync(slug, ct);

        if (app is null)
        {
            return McpResponseFormatter.AppNotFound(slug);
        }

        var process = _supervisor.GetProcess(app.Id);
        var bindings = await _appStore.GetBindingsAsync(app.AppTypeId, ct);
        var overrides = await _appStore.GetOverridesAsync(app.Id, ct);

        var hasProcess = bindings.Any
        (
            b => string.Equals(b.CapabilitySlug, "process", StringComparison.Ordinal)
        );

        var hasRouting = bindings.Any
        (
            b => string.Equals(b.CapabilitySlug, "routing", StringComparison.Ordinal)
        );

        var routingBinding = bindings.SingleOrDefault
        (
            b => string.Equals(b.CapabilitySlug, "routing", StringComparison.Ordinal)
        );

        RoutingConfiguration? routingConfiguration = null;

        if (routingBinding is not null)
        {
            var overrideJson = overrides.TryGetValue("routing", out var routingOverride)
                ? routingOverride.ConfigurationJson
                : null;

            routingConfiguration = CapabilityResolver.Resolve<RoutingConfiguration>
            (
                routingBinding.DefaultConfigurationJson, overrideJson
            );
        }

        var domain = routingConfiguration?.DomainPattern
            .Replace("{slug}", app.Slug, StringComparison.OrdinalIgnoreCase);

        var routeEnabled = routingConfiguration is not null && _proxy.IsRouteEnabled(app.Slug);

        var status = ResolveStatus(hasProcess, process, hasRouting, routeEnabled);

        string? restartPolicy = null;

        var restartBinding = bindings.SingleOrDefault
        (
            b => string.Equals(b.CapabilitySlug, "restart", StringComparison.Ordinal)
        );

        if (restartBinding is not null)
        {
            var overrideJson = overrides.TryGetValue("restart", out var restartOverride)
                ? restartOverride.ConfigurationJson
                : null;

            var restartConfig = CapabilityResolver.Resolve<RestartConfiguration>
            (
                restartBinding.DefaultConfigurationJson, overrideJson
            );

            if (restartConfig is not null)
            {
                var policyName = restartConfig.Policy.ToString();
                restartPolicy = char.ToLowerInvariant(policyName[0]) + policyName[1..];
            }
        }

        bool? autoStart = null;

        var autoStartBinding = bindings.SingleOrDefault
        (
            b => string.Equals(b.CapabilitySlug, "auto-start", StringComparison.Ordinal)
        );

        if (autoStartBinding is not null)
        {
            var overrideJson = overrides.TryGetValue("auto-start", out var autoStartOverride)
                ? autoStartOverride.ConfigurationJson
                : null;

            var autoStartConfig = CapabilityResolver.Resolve<AutoStartConfiguration>
            (
                autoStartBinding.DefaultConfigurationJson, overrideJson
            );

            autoStart = autoStartConfig?.Enabled;
        }

        var probes = _probeService.GetCachedProbes(app.Id);

        string? routeTarget = null;

        if (routingConfiguration is not null)
        {
            routeTarget = routingConfiguration.ServeMode == ServeMode.ReverseProxy && process?.Port is not null
                ? string.Create(CultureInfo.InvariantCulture, $"localhost:{process.Port.Value}")
                : routingConfiguration.ServeMode == ServeMode.FileServer
                    ? "file-server"
                    : "not-running";
        }

        var capabilities = bindings.Select(b => b.CapabilitySlug).ToList();

        var result = new
        {
            slug = app.Slug,
            displayName = app.DisplayName,
            appType = app.AppType.Slug,
            status = status.ToApiString(),
            pid = process?.Pid,
            port = process?.Port,
            uptimeSeconds = process?.UptimeSeconds,
            restartCount = process?.RestartCount ?? 0,
            restartPolicy,
            autoStart,
            domain,
            routeEnabled,
            routeTarget,
            capabilities,
            probes
        };

        return McpResponseFormatter.Success(McpResponseFormatter.ToJson(result));
    }

    [McpServerTool
    (
        Name = "list_app_types",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Lists all available application types with their display names, descriptions, capabilities, and registration schemas. Use this when registering a new app to discover valid app type slugs and understand what fields each type requires. The five built-in types are: dotnet-app, nodejs-app, static-site, executable, system-service.")]
    public async Task<CallToolResult> ListAppTypesAsync(CancellationToken ct)
    {
        var appTypes = await _appStore.ListAppTypesAsync(ct);

        var items = new List<object>();

        foreach (var appType in appTypes)
        {
            var capabilities = appType.Bindings.Select(b => b.CapabilitySlug).ToList();

            items.Add
            (
                new
                {
                    slug = appType.Slug,
                    displayName = appType.DisplayName,
                    description = appType.Description,
                    capabilities
                }
            );
        }

        var header = "Use appType slug in register_app. Each type supports different capabilities.";
        var json = McpResponseFormatter.ToJson(items);

        return McpResponseFormatter.Success($"{header}\n{json}");
    }

    private static ProcessState ResolveStatus
    (
        bool hasProcess,
        ManagedProcess? process,
        bool hasRouting,
        bool routeEnabled
    ) =>
        hasProcess
            ? process?.State ?? ProcessState.Stopped
            : hasRouting && routeEnabled
                ? ProcessState.Running
                : ProcessState.Stopped;

    private static string FormatUptime(double uptimeSeconds)
    {
        var ts = TimeSpan.FromSeconds(uptimeSeconds);

        return ts.TotalDays >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m")
            : ts.TotalHours >= 1
                ? string.Create(CultureInfo.InvariantCulture, $"{ts.Hours}h {ts.Minutes}m {ts.Seconds}s")
                : ts.TotalMinutes >= 1
                    ? string.Create(CultureInfo.InvariantCulture, $"{ts.Minutes}m {ts.Seconds}s")
                    : string.Create(CultureInfo.InvariantCulture, $"{ts.Seconds}s");
    }
}
