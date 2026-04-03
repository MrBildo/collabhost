namespace Collabhost.Api.Capabilities;

public sealed class CapabilityOverride
{
    public Ulid Id { get; init; } = Ulid.NewUlid();

    public required Ulid AppId { get; init; }

    public required string CapabilitySlug { get; init; }

    public required string ConfigurationJson { get; set; }
}
