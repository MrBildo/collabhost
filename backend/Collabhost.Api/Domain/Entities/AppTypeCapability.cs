namespace Collabhost.Api.Domain.Entities;

public class AppTypeCapability : Entity
{
    public Guid AppTypeId { get; private set; }

    public Guid CapabilityId { get; private set; }

    public string Configuration { get; private set; } = default!;

    protected AppTypeCapability() { }

    public static AppTypeCapability Create
    (
        Guid appTypeId,
        Guid capabilityId,
        string configuration
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);

        return new AppTypeCapability
        {
            AppTypeId = appTypeId,
            CapabilityId = capabilityId,
            Configuration = configuration
        };
    }

    public static AppTypeCapability CreateSeeded
    (
        Guid id,
        Guid appTypeId,
        Guid capabilityId,
        string configuration
    ) =>
        new()
        {
            Id = id,
            AppTypeId = appTypeId,
            CapabilityId = capabilityId,
            Configuration = configuration
        };

    public void UpdateConfiguration(string configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);
        Configuration = configuration;
    }
}
