using System.Globalization;

namespace Collabhost.Api.Domain;

public abstract class Entity
{
    public Guid Id { get; init; } = Guid.NewGuid();

    protected Entity() { }
}

public abstract class AggregateRoot : Entity
{
    public string ExternalId { get; init; } = Ulid.NewUlid().ToString(null, CultureInfo.InvariantCulture);

    protected AggregateRoot() { }
}

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
