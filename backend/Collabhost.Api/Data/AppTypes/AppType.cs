namespace Collabhost.Api.Data.AppTypes;

public class AppType
{
    public required string Slug { get; init; }

    public required string DisplayName { get; set; }

    public string? Description { get; set; }

    public bool IsBuiltIn { get; init; }
}
