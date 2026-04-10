using Collabhost.Api.Registry;

namespace Collabhost.Api.Data.AppTypes;

// During Phase 0 coexistence, named AppTypeDefinition to avoid collision with the EF entity
// (Registry.AppType). In Phase 2 when the EF entity is deleted, this class will be renamed
// to AppType and moved to the Registry namespace per spec decision D4.
public class AppTypeDefinition
{
    public required string Slug { get; init; }

    public required string DisplayName { get; set; }

    public string? Description { get; set; }

    public AppTypeMetadata? Metadata { get; set; }

    public bool IsBuiltIn { get; init; }
}
