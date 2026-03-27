using System.Globalization;

namespace Collabhost.Api.Domain;

public abstract class AggregateRoot : Entity
{
    public string ExternalId { get; init; } = Ulid.NewUlid().ToString(null, CultureInfo.InvariantCulture);

    protected AggregateRoot() { }
}
