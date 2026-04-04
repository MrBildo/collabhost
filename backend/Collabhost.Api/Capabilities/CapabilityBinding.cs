namespace Collabhost.Api.Capabilities;

public class CapabilityBinding
{
    public Ulid Id { get; init; } = Ulid.NewUlid();

    public required Ulid AppTypeId { get; init; }

    public required string CapabilitySlug { get; init; }

    public required string DefaultConfigurationJson { get; set; }
}

public class CapabilityOverride
{
    public Ulid Id { get; init; } = Ulid.NewUlid();

    public required Ulid AppId { get; init; }

    public required string CapabilitySlug { get; init; }

    public required string ConfigurationJson { get; set; }
}
