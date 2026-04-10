using System.Reflection;
using Collabhost.Api.Registry;

namespace Collabhost.Api.Data.AppTypes;

public class TypeStore(ILogger<TypeStore> logger)
{
    private readonly ILogger<TypeStore> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private volatile TypeStoreSnapshot _snapshot = new
    (
        [],
        FrozenDictionary<string, AppTypeDefinition>.Empty,
        FrozenDictionary<string, IReadOnlyDictionary<string, string>>.Empty
    );

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var sources = await ReadEmbeddedResourcesAsync(cancellationToken);

        var errors = TypeStoreValidator.Validate(sources);

        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                _logger.LogCritical
                (
                    "TypeStore validation error in {Source}: {FieldPath} -- {Message}",
                    error.Source,
                    error.FieldPath,
                    error.Message
                );
            }

            throw new TypeStoreValidationException(errors);
        }

        var snapshot = BuildSnapshot(sources);

        Interlocked.Exchange(ref _snapshot, snapshot);

        _logger.LogInformation
        (
            "TypeStore loaded: {TypeCount} built-in types, {BindingCount} bindings",
            snapshot.Types.Count,
            snapshot.BindingsByTypeSlug.Values
                .Sum(bindings => bindings.Count)
        );
    }

    public AppTypeDefinition? GetBySlug(string slug) =>
        _snapshot.TypesBySlug.GetValueOrDefault(slug);

    public IReadOnlyList<AppTypeDefinition> ListTypes() =>
        _snapshot.Types;

    public IReadOnlyDictionary<string, string>? GetBindings(string appTypeSlug) =>
        _snapshot.BindingsByTypeSlug.GetValueOrDefault(appTypeSlug);

    public bool HasBinding(string appTypeSlug, string capabilitySlug) =>
        _snapshot.BindingsByTypeSlug.TryGetValue(appTypeSlug, out var bindings)
        && bindings.ContainsKey(capabilitySlug);

    private static async Task<IReadOnlyList<(string ResourceName, string Json)>> ReadEmbeddedResourcesAsync
    (
        CancellationToken cancellationToken
    )
    {
        var assembly = Assembly.GetExecutingAssembly();

        var resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                && name.Contains("BuiltInTypes", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

        var sources = new List<(string ResourceName, string Json)>(resourceNames.Count);

        foreach (var resourceName in resourceNames)
        {
            await using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");

            using var reader = new StreamReader(stream);

            var json = await reader.ReadToEndAsync(cancellationToken);

            sources.Add((resourceName, json));
        }

        return sources;
    }

    private static TypeStoreSnapshot BuildSnapshot
    (
        IReadOnlyList<(string ResourceName, string Json)> sources
    )
    {
        var types = new List<AppTypeDefinition>(sources.Count);
        var typesBySlug = new Dictionary<string, AppTypeDefinition>(sources.Count, StringComparer.Ordinal);
        var bindingsByTypeSlug = new Dictionary<string, IReadOnlyDictionary<string, string>>(sources.Count, StringComparer.Ordinal);

        foreach (var (resourceName, json) in sources)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var slug = root.GetProperty("slug").GetString()!;
            var displayName = root.GetProperty("displayName").GetString()!;

            var description = root.TryGetProperty("description", out var descriptionElement)
                && descriptionElement.ValueKind == JsonValueKind.String
                    ? descriptionElement.GetString()
                    : null;

            AppTypeMetadata? metadata = null;

            if (root.TryGetProperty("metadata", out var metadataElement)
                && metadataElement.ValueKind == JsonValueKind.Object)
            {
                metadata = JsonSerializer.Deserialize<AppTypeMetadata>(metadataElement.GetRawText(), _jsonOptions);
            }

            var typeDefinition = new AppTypeDefinition
            {
                Slug = slug,
                DisplayName = displayName,
                Description = description,
                Metadata = metadata,
                IsBuiltIn = true
            };

            types.Add(typeDefinition);
            typesBySlug[slug] = typeDefinition;

            // Build bindings dictionary for this type
            var bindings = new Dictionary<string, string>(StringComparer.Ordinal);

            if (root.TryGetProperty("bindings", out var bindingsElement)
                && bindingsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var binding in bindingsElement.EnumerateObject())
                {
                    bindings[binding.Name] = binding.Value.GetRawText();
                }
            }

            bindingsByTypeSlug[slug] = bindings;
        }

        return new TypeStoreSnapshot
        (
            types.AsReadOnly(),
            typesBySlug.ToFrozenDictionary(StringComparer.Ordinal),
            bindingsByTypeSlug.ToFrozenDictionary(StringComparer.Ordinal)
        );
    }
}
