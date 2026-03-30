using System.Globalization;

namespace Collabhost.Api.Domain.Entities;

public class AppType : Entity
{
    public string ExternalId { get; private set; } = Ulid.NewUlid().ToString(null, CultureInfo.InvariantCulture);

    public string Name { get; private set; } = default!;

    public string DisplayName { get; private set; } = default!;

    public string? Description { get; private set; }

    public bool IsBuiltIn { get; private set; }

    protected AppType() { }

    public static AppType Create
    (
        string name,
        string displayName,
        string? description,
        bool isBuiltIn
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        return new AppType
        {
            Name = name.Trim().ToLowerInvariant(),
            DisplayName = displayName.Trim(),
            Description = description?.Trim(),
            IsBuiltIn = isBuiltIn
        };
    }

    public static AppType CreateSeeded
    (
        Guid id,
        string externalId,
        string name,
        string displayName,
        string? description,
        bool isBuiltIn
    ) =>
        new()
        {
            Id = id,
            ExternalId = externalId,
            Name = name,
            DisplayName = displayName,
            Description = description,
            IsBuiltIn = isBuiltIn
        };

    public void UpdateDetails(string displayName, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName.Trim();
        Description = description?.Trim();
    }
}
