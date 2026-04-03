using Collabhost.Api.Registry;

namespace Collabhost.Api.Capabilities;

public class CapabilityStore
(
    AppStore appStore,
    ILogger<CapabilityStore> logger
)
{
    private readonly AppStore _appStore = appStore
        ?? throw new ArgumentNullException(nameof(appStore));

    private readonly ILogger<CapabilityStore> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    public async Task<T?> ResolveAsync<T>
    (
        string capabilitySlug,
        Ulid appTypeId,
        Ulid appId,
        CancellationToken ct
    )
        where T : class
    {
        var bindings = await _appStore.GetBindingsAsync(appTypeId, ct);

        var binding = bindings
            .SingleOrDefault(b => string.Equals(b.CapabilitySlug, capabilitySlug, StringComparison.Ordinal));

        if (binding is null)
        {
            return null;
        }

        var overrides = await _appStore.GetOverridesAsync(appId, ct);

        var overrideJson = overrides.TryGetValue(capabilitySlug, out var capabilityOverride)
            ? capabilityOverride.ConfigurationJson
            : null;

        return CapabilityResolver.Resolve<T>(binding.DefaultConfigurationJson, overrideJson);
    }

    public async Task<string?> ResolveJsonAsync
    (
        string capabilitySlug,
        Ulid appTypeId,
        Ulid appId,
        CancellationToken ct
    )
    {
        var bindings = await _appStore.GetBindingsAsync(appTypeId, ct);

        var binding = bindings
            .SingleOrDefault(b => string.Equals(b.CapabilitySlug, capabilitySlug, StringComparison.Ordinal));

        if (binding is null)
        {
            return null;
        }

        var overrides = await _appStore.GetOverridesAsync(appId, ct);

        var overrideJson = overrides.TryGetValue(capabilitySlug, out var capabilityOverride)
            ? capabilityOverride.ConfigurationJson
            : null;

        return CapabilityResolver.ResolveJson(binding.DefaultConfigurationJson, overrideJson);
    }

    public async Task<IDictionary<string, string>> ResolveAllJsonAsync
    (
        Ulid appTypeId,
        Ulid appId,
        CancellationToken ct
    )
    {
        var bindings = await _appStore.GetBindingsAsync(appTypeId, ct);
        var overrides = await _appStore.GetOverridesAsync(appId, ct);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var binding in bindings)
        {
            var overrideJson = overrides.TryGetValue(binding.CapabilitySlug, out var capabilityOverride)
                ? capabilityOverride.ConfigurationJson
                : null;

            result[binding.CapabilitySlug] = CapabilityResolver.ResolveJson(
                binding.DefaultConfigurationJson,
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
        var proposedOverrides = JsonNode.Parse(configurationJson)?.AsObject();

        if (proposedOverrides is null)
        {
            throw new ArgumentException("Invalid JSON.", nameof(configurationJson));
        }

        var errors = CapabilityResolver.ValidateEdits(capabilitySlug, proposedOverrides, isNewApp);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Validation failed: {string.Join("; ", errors)}"
            );
        }

        await _appStore.SaveOverrideAsync(appId, capabilitySlug, configurationJson, ct);

        _logger.LogInformation(
            "Saved override for {CapabilitySlug} on app {AppId}",
            capabilitySlug,
            appId
        );
    }
}
