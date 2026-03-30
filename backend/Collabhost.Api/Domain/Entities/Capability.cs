namespace Collabhost.Api.Domain.Entities;

public class Capability : Entity
{
    public string Slug { get; private set; } = default!;

    public string DisplayName { get; private set; } = default!;

    public string? Description { get; private set; }

    public string Category { get; private set; } = default!;

    protected Capability() { }

    public static Capability CreateSeeded
    (
        Guid id,
        string slug,
        string displayName,
        string? description,
        string category
    ) =>
        new()
        {
            Id = id,
            Slug = slug,
            DisplayName = displayName,
            Description = description,
            Category = category
        };
}
