using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Registry;

namespace Collabhost.Api.Capabilities;

public class CapabilityStore
(
    TypeStore typeStore,
    AppStore appStore,
    ILogger<CapabilityStore> logger
)
{
    private readonly TypeStore _typeStore = typeStore
        ?? throw new ArgumentNullException(nameof(typeStore));

    private readonly AppStore _appStore = appStore
        ?? throw new ArgumentNullException(nameof(appStore));

    private readonly ILogger<CapabilityStore> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    public async Task<T?> ResolveAsync<T>
    (
        string capabilitySlug,
        string appTypeSlug,
        Ulid appId,
        CancellationToken ct
    )
        where T : class
    {
        var bindings = _typeStore.GetBindings(appTypeSlug);

        if (bindings is null)
        {
            return null;
        }

        if (!bindings.TryGetValue(capabilitySlug, out var defaultConfigurationJson))
        {
            return null;
        }

        var overrides = await _appStore.GetOverridesAsync(appId, ct);

        var overrideJson = overrides.TryGetValue(capabilitySlug, out var capabilityOverride)
            ? capabilityOverride.ConfigurationJson
            : null;

        return CapabilityResolver.Resolve<T>(defaultConfigurationJson, overrideJson);
    }

    public async Task<T?> ResolveAsync<T>
    (
        string capabilitySlug,
        App app,
        CancellationToken ct
    )
        where T : class =>
        await ResolveAsync<T>(capabilitySlug, app.AppTypeSlug!, app.Id, ct);

    public async Task<string?> ResolveJsonAsync
    (
        string capabilitySlug,
        string appTypeSlug,
        Ulid appId,
        CancellationToken ct
    )
    {
        var bindings = _typeStore.GetBindings(appTypeSlug);

        if (bindings is null)
        {
            return null;
        }

        if (!bindings.TryGetValue(capabilitySlug, out var defaultConfigurationJson))
        {
            return null;
        }

        var overrides = await _appStore.GetOverridesAsync(appId, ct);

        var overrideJson = overrides.TryGetValue(capabilitySlug, out var capabilityOverride)
            ? capabilityOverride.ConfigurationJson
            : null;

        return CapabilityResolver.ResolveJson(defaultConfigurationJson, overrideJson);
    }

    public async Task<IDictionary<string, string>> ResolveAllJsonAsync
    (
        string appTypeSlug,
        Ulid appId,
        CancellationToken ct
    )
    {
        var bindings = _typeStore.GetBindings(appTypeSlug);
        var overrides = await _appStore.GetOverridesAsync(appId, ct);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        if (bindings is null)
        {
            return result;
        }

        foreach (var (capabilitySlug, defaultConfigurationJson) in bindings)
        {
            var overrideJson = overrides.TryGetValue(capabilitySlug, out var capabilityOverride)
                ? capabilityOverride.ConfigurationJson
                : null;

            result[capabilitySlug] = CapabilityResolver.ResolveJson
            (
                defaultConfigurationJson,
                overrideJson
            );
        }

        return result;
    }

    public async Task SaveOverrideAsync
    (
        string capabilitySlug,
        Ulid appId,
        string configurationJson,
        bool isNewApp,
        CancellationToken ct
    )
    {
        var proposedOverrides = JsonNode.Parse(configurationJson)?.AsObject()
            ?? throw new ArgumentException("Invalid JSON.", nameof(configurationJson));

        var errors = CapabilityResolver.ValidateEdits(capabilitySlug, proposedOverrides, isNewApp);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException
            (
                $"Validation failed: {string.Join("; ", errors)}"
            );
        }

        await _appStore.SaveOverrideAsync(appId, capabilitySlug, configurationJson, ct);

        _logger.LogInformation
        (
            "Saved override for {CapabilitySlug} on app {AppId}",
            capabilitySlug,
            appId
        );
    }
}
