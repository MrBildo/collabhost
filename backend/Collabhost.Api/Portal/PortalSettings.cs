namespace Collabhost.Api.Portal;

public class PortalSettings
{
    public const string SectionName = "Portal";

    public required string Subdomain { get; init; }
}
