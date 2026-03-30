namespace Collabhost.Api.Domain.Entities;

public class CapabilityConfiguration : Entity
{
    public Guid AppId { get; private set; }

    public Guid AppTypeCapabilityId { get; private set; }

    public string Configuration { get; private set; } = default!;

    protected CapabilityConfiguration() { }

    public static CapabilityConfiguration Create
    (
        Guid appId,
        Guid appTypeCapabilityId,
        string configuration
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);

        return new CapabilityConfiguration
        {
            AppId = appId,
            AppTypeCapabilityId = appTypeCapabilityId,
            Configuration = configuration
        };
    }

    public void UpdateConfiguration(string configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);
        Configuration = configuration;
    }
}
