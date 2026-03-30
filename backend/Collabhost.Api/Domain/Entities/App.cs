using Collabhost.Api.Domain.Values;

namespace Collabhost.Api.Domain.Entities;

public class App : AggregateRoot
{
    public AppSlugValue Name { get; private set; } = default!;

    public string DisplayName { get; private set; } = default!;

    public Guid AppTypeId { get; private set; }

    public string InstallDirectory { get; private set; } = default!;

    public int? Port { get; private set; }

    public bool IsStopped { get; private set; }

    public DateTime RegisteredAt { get; private init; }

    protected App() { } // EF Core

    public static App Register
    (
        AppSlugValue name,
        string displayName,
        Guid appTypeId,
        string installDirectory
    )
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

        return new App
        {
            Name = name,
            DisplayName = displayName.Trim(),
            AppTypeId = appTypeId,
            InstallDirectory = installDirectory,
            IsStopped = false,
            RegisteredAt = DateTime.UtcNow
        };
    }

    public void UpdateDetails
    (
        string displayName,
        string installDirectory
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

        DisplayName = displayName.Trim();
        InstallDirectory = installDirectory;
    }

    public void AssignPort(int port)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);
        Port = port;
    }

    public void MarkStopped() => IsStopped = true;

    public void MarkStarted() => IsStopped = false;
}
