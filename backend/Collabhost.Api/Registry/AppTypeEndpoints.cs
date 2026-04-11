using Collabhost.Api.Capabilities;
using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Data.AppTypes;

using Microsoft.AspNetCore.Http.HttpResults;

namespace Collabhost.Api.Registry;

#pragma warning disable MA0076 // ToString on types that are not locale-sensitive
#pragma warning disable MA0011 // ToString on types that are not locale-sensitive
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

        group.MapGet("/", ListAppTypes);
        group.MapGet("/{slug}/registration", GetRegistrationSchema);
    }

    private static Ok<List<AppTypeListItem>> ListAppTypes
    (
        TypeStore typeStore
    )
    {
        var types = typeStore.ListTypes();

        var items = types
            .Select
            (
                t => new AppTypeListItem
                (
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

    private static IResult GetRegistrationSchema
    (
        string slug,
        TypeStore typeStore
    )
    {
        var appType = typeStore.GetBySlug(slug);

        if (appType is null)
        {
            return TypedResults.NotFound();
        }

        var bindings = typeStore.GetBindings(slug);

        var tags = BuildTags(appType);

        var sections = BuildRegistrationSections(appType, bindings);

        var schema = new RegistrationSchema
        (
            new RegistrationAppType
            (
                appType.Slug,
                appType.DisplayName,
                appType.Description
            ),
            tags,
            sections
        );

        return TypedResults.Ok(schema);
    }

    private static List<RegistrationSection> BuildRegistrationSections
    (
        AppType appType,
        IReadOnlyDictionary<string, string>? bindings
    )
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

        if (bindings is null)
        {
            return sections;
        }

        // Artifact section (if the type has artifact capability)
        if (bindings.ContainsKey("artifact"))
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

        // Discovery strategy section (if the type has process capability)
        if (bindings.TryGetValue("process", out var processBindingJson))
        {
            var processConfiguration = JsonSerializer.Deserialize<ProcessConfiguration>
            (
                processBindingJson, _jsonOptions
            );

            var defaultStrategy = processConfiguration?.DiscoveryStrategy ?? DiscoveryStrategy.Manual;

            var validStrategies = GetValidStrategiesForAppType(appType.Slug);

            sections.Add
            (
                new RegistrationSection
                (
                    "discovery",
                    "How to Run",
                    [
                        new RegistrationField
                        (
                            "discoveryStrategy",
                            "Discovery Strategy",
                            "select",
                            false,
                            defaultStrategy.ToCamelCase(),
                            HelpText: "How Collabhost finds and launches this application",
                            Options:
                            [
                                .. validStrategies.Select
                                (
                                    v => new FieldOption
                                    (
                                        v.ToCamelCase(),
                                        FormatDiscoveryStrategyLabel(v)
                                    )
                                )
                            ]
                        )
                    ]
                )
            );
        }

        // Routing options section (for file-server types that support SPA fallback)
        if (bindings.TryGetValue("routing", out var routingBindingJson)
            && IsFileServerRouting(routingBindingJson))
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

    private static bool IsFileServerRouting(string routingConfigurationJson)
    {
        try
        {
            var configuration = JsonSerializer.Deserialize<RoutingConfiguration>
            (
                routingConfigurationJson, _jsonOptions
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

        if (appType.Metadata is null)
        {
            return tags;
        }

        var metadata = appType.Metadata;

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

    private static IReadOnlyList<DiscoveryStrategy> GetValidStrategiesForAppType(string appTypeSlug) =>
        appTypeSlug switch
        {
            "dotnet-app" =>
            [
                DiscoveryStrategy.DotNetRuntimeConfiguration,
                DiscoveryStrategy.DotNetProject,
                DiscoveryStrategy.Manual
            ],
            "nodejs-app" =>
            [
                DiscoveryStrategy.PackageJson,
                DiscoveryStrategy.Manual
            ],
            _ =>
            [
                .. Enum.GetValues<DiscoveryStrategy>()
            ]
        };

    private static string FormatDiscoveryStrategyLabel(DiscoveryStrategy value) => value switch
    {
        DiscoveryStrategy.DotNetRuntimeConfiguration => ".NET Runtime Config",
        DiscoveryStrategy.DotNetProject => ".NET Project (source)",
        DiscoveryStrategy.PackageJson => "Package JSON",
        DiscoveryStrategy.Manual => "Manual",
        _ => value.ToString()
    };
}
#pragma warning restore MA0011
#pragma warning restore MA0076
