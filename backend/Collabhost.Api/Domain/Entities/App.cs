using Collabhost.Api.Domain.Values;

namespace Collabhost.Api.Domain.Entities;

public class App : AggregateRoot
{
    public AppSlugValue Name { get; private set; } = default!;

    public string DisplayName { get; private set; } = default!;

    public Guid AppTypeId { get; private set; }

    public DateTime RegisteredAt { get; private init; }

    protected App() { } // EF Core

    public static App Register
    (
        AppSlugValue name,
        string displayName,
        Guid appTypeId
    )
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        return new App
        {
            Name = name,
            DisplayName = displayName.Trim(),
            AppTypeId = appTypeId,
            RegisteredAt = DateTime.UtcNow
        };
    }

    public void UpdateDetails(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName.Trim();
    }
}
