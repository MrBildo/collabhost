using Collabhost.Api.Capabilities;
using Collabhost.Api.Capabilities.Configurations;

namespace Collabhost.Api.Registry;

#pragma warning disable MA0076 // Ulid.ToString is not locale-sensitive
#pragma warning disable MA0011 // Ulid.ToString is not locale-sensitive
public static class AppTypeEndpoints
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static void Map(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/app-types").WithTags("App Types");

        group.MapGet("/", ListAppTypesAsync);
        group.MapGet("/{slug}/registration", GetRegistrationSchemaAsync);
    }

    private static async Task<IResult> ListAppTypesAsync
    (
        AppStore store,
        CancellationToken ct
    )
    {
        var appTypes = await store.ListAppTypesAsync(ct);

        var items = appTypes
            .Select
            (
                t => new AppTypeListItem
                (
                    t.Id.ToString(),
                    t.Slug,
                    t.DisplayName,
                    t.Description,
                    BuildTags(t),
                    t.IsBuiltIn
                )
            )
                .ToList();

        return TypedResults.Ok(items);
    }

    private static async Task<IResult> GetRegistrationSchemaAsync
    (
        string slug,
        AppStore store,
        CancellationToken ct
    )
    {
        var appType = await store.GetAppTypeBySlugAsync(slug, ct);

        if (appType is null)
        {
            return TypedResults.NotFound();
        }

        var tags = BuildTags(appType);

        var sections = BuildRegistrationSections(appType);

        var schema = new RegistrationSchema
        (
            new RegistrationAppType
            (
                appType.Id.ToString(),
                appType.Slug,
                appType.DisplayName,
                appType.Description
            ),
            tags,
            sections
        );

        return TypedResults.Ok(schema);
    }

    private static List<RegistrationSection> BuildRegistrationSections(AppType appType)
    {
        var sections = new List<RegistrationSection>
        {
            // Basics section (always first)
            new            (
                "basics",
                "Basics",
                [
                    new RegistrationField
                    (
                        "displayName",
                        "Display Name",
                        "text",
                        true,
                        null,
                        "My Application"
                    ),
                    new RegistrationField
                    (
                        "name",
                        "Slug",
                        "text",
                        true,
                        null,
                        "my-application",
                        "Used in the domain name and as a unique identifier. Cannot be changed later."
                    )
                ]
            )
        };

        // Artifact section (if the type has artifact capability)
        var artifactBinding = appType.Bindings.SingleOrDefault
        (
            b => string.Equals(b.CapabilitySlug, "artifact", StringComparison.Ordinal)
        );

        if (artifactBinding is not null)
        {
            sections.Add
            (
                new RegistrationSection
                (
                    "artifact",
                    "Where are the files?",
                    [
                        new RegistrationField
                        (
                            "location",
                            "Application Directory",
                            "directory",
                            true,
                            null
                        )
                    ]
                )
            );
        }

        // Routing options section (for file-server types that support SPA fallback)
        var routingBinding = appType.Bindings.SingleOrDefault
        (
            b => string.Equals(b.CapabilitySlug, "routing", StringComparison.Ordinal)
        );

        if (routingBinding is not null && IsFileServerRouting(routingBinding))
        {
            sections.Add
            (
                new RegistrationSection
                (
                    "routing",
                    "Routing Options",
                    [
                        new RegistrationField
                        (
                            "spaFallback",
                            "SPA Fallback",
                            "boolean",
                            false,
                            false,
                            HelpText: "Route all paths to index.html for single-page applications (React, Vue, etc.)"
                        )
                    ]
                )
            );
        }

        return sections;
    }

    private static bool IsFileServerRouting(CapabilityBinding routingBinding)
    {
        try
        {
            var configuration = JsonSerializer.Deserialize<RoutingConfiguration>
            (
                routingBinding.DefaultConfigurationJson, _jsonOptions
            );

            return configuration?.ServeMode == ServeMode.FileServer;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static List<AppTag> BuildTags(AppType appType)
    {
        var tags = new List<AppTag>();

        if (appType.MetadataJson is null)
        {
            return tags;
        }

        var metadata = appType.MetadataJson.TryDeserializeJson<AppTypeMetadata>(_jsonOptions);

        if (metadata is null)
        {
            return tags;
        }

        if (metadata.Runtime is not null)
        {
            var label = !string.IsNullOrWhiteSpace(metadata.Runtime.Version)
                ? $"{metadata.Runtime.Name} {metadata.Runtime.Version}"
                : metadata.Runtime.Name;

            tags.Add(new AppTag(label, "runtime"));

            if (!string.IsNullOrWhiteSpace(metadata.Runtime.PackageManager))
            {
                tags.Add(new AppTag(metadata.Runtime.PackageManager, "tooling"));
            }
        }

        if (metadata.Framework is not null)
        {
            var label = !string.IsNullOrWhiteSpace(metadata.Framework.Version)
                ? $"{metadata.Framework.Name} {metadata.Framework.Version}"
                : metadata.Framework.Name;

            tags.Add(new AppTag(label, "framework"));
        }

        return tags;
    }
}
#pragma warning restore MA0011
#pragma warning restore MA0076

// Encapsulates try/catch for JSON deserialization so callers use validation flow, not exception flow
file static class JsonExtensions
{
    public static T? TryDeserializeJson<T>(this string json, JsonSerializerOptions? options = null)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
