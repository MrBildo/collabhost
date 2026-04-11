using Collabhost.Api.Capabilities;

namespace Collabhost.Api.Data.AppTypes;

public static partial class TypeStoreValidator
{
    [GeneratedRegex(@"^[a-z0-9-]+$", RegexOptions.None, 100)]
    private static partial Regex SlugPattern { get; }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static IReadOnlyList<TypeStoreValidationError> Validate
    (
        IReadOnlyList<(string ResourceName, string Json)> sources
    )
    {
        var errors = new List<TypeStoreValidationError>();

        var parsedTypes = new List<(string ResourceName, string Slug, string DisplayName)>();

        foreach (var (resourceName, json) in sources)
        {
            ValidateFile(resourceName, json, errors, parsedTypes);
        }

        ValidateCrossFile(parsedTypes, errors);

        return errors;
    }

    public static IReadOnlyList<TypeStoreValidationError> ValidateUserTypes
    (
        IReadOnlyList<(string FileName, string Json)> userSources,
        IReadOnlyList<AppType> builtInTypes
    )
    {
        var errors = new List<TypeStoreValidationError>();

        var parsedTypes = new List<(string ResourceName, string Slug, string DisplayName)>();

        foreach (var (fileName, json) in userSources)
        {
            ValidateFile(fileName, json, errors, parsedTypes);
        }

        // Cross-file validation among user types themselves
        ValidateCrossFile(parsedTypes, errors);

        // Cross-set validation: user types vs built-in types
        ValidateAgainstBuiltInTypes(parsedTypes, builtInTypes, errors);

        return errors;
    }

    private static void ValidateAgainstBuiltInTypes
    (
        List<(string ResourceName, string Slug, string DisplayName)> userTypes,
        IReadOnlyList<AppType> builtInTypes,
        List<TypeStoreValidationError> errors
    )
    {
        var builtInSlugs = builtInTypes
            .Select(type => type.Slug)
                .ToHashSet(StringComparer.Ordinal);

        var builtInDisplayNames = builtInTypes
            .Select(type => type.DisplayName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (resourceName, slug, displayName) in userTypes)
        {
            if (builtInSlugs.Contains(slug))
            {
                errors.Add(new TypeStoreValidationError(resourceName, "slug", $"Slug '{slug}' conflicts with a built-in type."));
            }

            if (builtInDisplayNames.Contains(displayName))
            {
                errors.Add(new TypeStoreValidationError(resourceName, "displayName", $"Display name '{displayName}' conflicts with a built-in type."));
            }
        }
    }

    private static void ValidateFile
    (
        string resourceName,
        string json,
        List<TypeStoreValidationError> errors,
        List<(string ResourceName, string Slug, string DisplayName)> parsedTypes
    )
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            errors.Add(new TypeStoreValidationError(resourceName, "(root)", $"Invalid JSON: {ex.Message}"));
            return;
        }

        using (document)
        {
            var root = document.RootElement;

            var slug = ValidateSlug(resourceName, root, errors);
            var displayName = ValidateDisplayName(resourceName, root, errors);
            ValidateBindings(resourceName, root, errors);

            if (slug is not null && displayName is not null)
            {
                parsedTypes.Add((resourceName, slug, displayName));
            }
        }
    }

    private static string? ValidateSlug
    (
        string resourceName,
        JsonElement root,
        List<TypeStoreValidationError> errors
    )
    {
        if (!root.TryGetProperty("slug", out var slugElement)
            || slugElement.ValueKind != JsonValueKind.String)
        {
            errors.Add(new TypeStoreValidationError(resourceName, "slug", "Required field is missing or not a string."));
            return null;
        }

        var slug = slugElement.GetString()
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(slug))
        {
            errors.Add(new TypeStoreValidationError(resourceName, "slug", "Must not be empty."));
            return null;
        }

        if (!SlugPattern.IsMatch(slug))
        {
            errors.Add(new TypeStoreValidationError(resourceName, "slug", $"Value '{slug}' does not match pattern [a-z0-9-]+."));
            return null;
        }

        // Slug must match the resource/file name (without extension)
        var expectedSlug = ExtractSlugFromResourceName(resourceName);

        if (!string.Equals(slug, expectedSlug, StringComparison.Ordinal))
        {
            errors.Add(new TypeStoreValidationError(resourceName, "slug", $"Value '{slug}' does not match resource name (expected '{expectedSlug}')."));
        }

        return slug;
    }

    private static string? ValidateDisplayName
    (
        string resourceName,
        JsonElement root,
        List<TypeStoreValidationError> errors
    )
    {
        if (!root.TryGetProperty("displayName", out var displayNameElement)
            || displayNameElement.ValueKind != JsonValueKind.String)
        {
            errors.Add(new TypeStoreValidationError(resourceName, "displayName", "Required field is missing or not a string."));
            return null;
        }

        var displayName = displayNameElement.GetString()
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(displayName))
        {
            errors.Add(new TypeStoreValidationError(resourceName, "displayName", "Must not be empty."));
            return null;
        }

        return displayName;
    }

    private static void ValidateBindings
    (
        string resourceName,
        JsonElement root,
        List<TypeStoreValidationError> errors
    )
    {
        if (!root.TryGetProperty("bindings", out var bindingsElement))
        {
            // Bindings are optional -- a type with no bindings is valid (though unusual)
            return;
        }

        if (bindingsElement.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new TypeStoreValidationError(resourceName, "bindings", "Must be an object."));
            return;
        }

        foreach (var binding in bindingsElement.EnumerateObject())
        {
            var capabilitySlug = binding.Name;

            if (!CapabilityCatalog.IsKnown(capabilitySlug))
            {
                errors.Add(new TypeStoreValidationError(resourceName, $"bindings.{capabilitySlug}", "Unknown capability slug."));
                continue;
            }

            var definition = CapabilityCatalog.Get(capabilitySlug)!;

            try
            {
                var bindingJson = binding.Value.GetRawText();

                var deserialized = JsonSerializer.Deserialize(bindingJson, definition.ConfigurationType, _jsonOptions);

                if (deserialized is null)
                {
                    errors.Add(new TypeStoreValidationError(resourceName, $"bindings.{capabilitySlug}", "Deserialized to null."));
                }
            }
            catch (JsonException ex)
            {
                errors.Add(new TypeStoreValidationError(resourceName, $"bindings.{capabilitySlug}", $"Failed to deserialize: {ex.Message}"));
            }
        }
    }

    private static void ValidateCrossFile
    (
        List<(string ResourceName, string Slug, string DisplayName)> parsedTypes,
        List<TypeStoreValidationError> errors
    )
    {
        // Check for duplicate slugs
        var slugGroups = parsedTypes
            .GroupBy(t => t.Slug, StringComparer.Ordinal)
            .Where(group => group.Skip(1).Any());

        foreach (var group in slugGroups)
        {
            var sources = string.Join(", ", group.Select(t => t.ResourceName));
            errors.Add(new TypeStoreValidationError(sources, "slug", $"Duplicate slug '{group.Key}'."));
        }

        // Check for duplicate display names
        var displayNameGroups = parsedTypes
            .GroupBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Skip(1).Any());

        foreach (var group in displayNameGroups)
        {
            var sources = string.Join(", ", group.Select(t => t.ResourceName));
            errors.Add(new TypeStoreValidationError(sources, "displayName", $"Duplicate display name '{group.Key}'."));
        }
    }

    internal static string ExtractSlugFromResourceName(string resourceName)
    {
        // Resource names from embedded resources look like:
        // Collabhost.Api.Data.BuiltInTypes.dotnet-app.json
        // We need to extract "dotnet-app" (the filename without extension)
        var lastDotIndex = resourceName.LastIndexOf('.');

        if (lastDotIndex <= 0)
        {
            return resourceName;
        }

        // Remove the .json extension
        var withoutExtension = resourceName[..lastDotIndex];

        // Find the last dot before the slug
        var secondLastDotIndex = withoutExtension.LastIndexOf('.');

        return secondLastDotIndex >= 0
            ? withoutExtension[(secondLastDotIndex + 1)..]
            : withoutExtension;
    }
}
