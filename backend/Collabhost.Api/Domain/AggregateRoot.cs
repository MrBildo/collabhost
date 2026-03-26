namespace Collabhost.Api.Domain;

public abstract class AggregateRoot : Entity
{
    public string ExternalId { get; init; } = Ulid.NewUlid().ToString();

    protected AggregateRoot() { }
}
