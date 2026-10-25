namespace Collabhost.Api.Data.AppTypes;

public class AppType
{
    public required string Slug { get; init; }

    public required string DisplayName { get; set; }

    public string? Description { get; set; }

    public bool IsBuiltIn { get; init; }

    // Marks the type as a platform-internal kind that should not appear in
    // operator-facing pickers (REST /api/v1/app-types, MCP list_app_types,
    // dashboard counts), and that registration (REST and MCP) refuses. Internal
    // types are still loaded into TypeStore and resolvable by GetBySlug so
    // subsystems like ProxyAppSeeder can seed their managed apps, and apps of
    // the type that already exist keep working. Driven by an "isInternal":
    // true flag on the JSON type definition. Defaults to false so all current
    // and future user types are operator-visible and registrable unless they
    // opt in.
    public bool IsInternal { get; init; }
}
