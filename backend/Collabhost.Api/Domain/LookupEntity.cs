namespace Collabhost.Api.Domain;

public abstract class LookupEntity
{
    public Guid Id { get; init; }
    public string Name { get; init; } = default!;
    public string DisplayName { get; init; } = default!;
    public string? Description { get; init; }
    public int Ordinal { get; init; }
    public bool IsActive { get; init; } = true;

    protected LookupEntity() { }
}
