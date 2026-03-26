using Collabhost.Api.Domain.Values;

namespace Collabhost.Api.Domain.Entities;

public class App : AggregateRoot
{
    public string Name { get; private set; } = default!;
    public string DisplayName { get; private set; } = default!;
    public Guid AppTypeId { get; private set; }
    public string InstallDirectory { get; private set; } = default!;
    public string CommandLine { get; private set; } = default!;
    public string? Arguments { get; private set; }
    public string? WorkingDirectory { get; private set; }
    public Guid RestartPolicyId { get; private set; }
    public int? Port { get; private set; }
    public string? HealthEndpoint { get; private set; }
    public string? UpdateCommand { get; private set; }
    public bool AutoStart { get; private set; }
    public DateTime RegisteredAt { get; private init; }

    private readonly List<EnvironmentVariable> _environmentVariables = [];
    public IReadOnlyCollection<EnvironmentVariable> EnvironmentVariables => [.. _environmentVariables];

    protected App() { } // EF Core

    public static App Register
    (
        AppSlugValue name,
        string displayName,
        Guid appTypeId,
        string installDirectory,
        string commandLine,
        string? arguments,
        string? workingDirectory,
        Guid restartPolicyId,
        string? healthEndpoint,
        string? updateCommand,
        bool autoStart
    )
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandLine);

        return new App
        {
            Name = name.Value,
            DisplayName = displayName.Trim(),
            AppTypeId = appTypeId,
            InstallDirectory = installDirectory,
            CommandLine = commandLine,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RestartPolicyId = restartPolicyId,
            HealthEndpoint = healthEndpoint,
            UpdateCommand = updateCommand,
            AutoStart = autoStart,
            RegisteredAt = DateTime.UtcNow
        };
    }

    // TODO: Consider refactoring UpdateConfiguration — bulk-setter is an encapsulation cheat.
    // Evaluate granular mutation methods (e.g. UpdateDisplayName, ChangeRestartPolicy) once
    // domain behavior is clearer.
    public void UpdateConfiguration
    (
        string displayName,
        string installDirectory,
        string commandLine,
        string? arguments,
        string? workingDirectory,
        Guid restartPolicyId,
        string? healthEndpoint,
        string? updateCommand,
        bool autoStart
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandLine);

        DisplayName = displayName.Trim();
        InstallDirectory = installDirectory;
        CommandLine = commandLine;
        Arguments = arguments;
        WorkingDirectory = workingDirectory;
        RestartPolicyId = restartPolicyId;
        HealthEndpoint = healthEndpoint;
        UpdateCommand = updateCommand;
        AutoStart = autoStart;
    }

    public void AssignPort(int port)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);
        Port = port;
    }

    public void AddEnvironmentVariable(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var existing = _environmentVariables.FirstOrDefault(e => e.Name == name);
        if (existing is not null)
        {
            existing.UpdateValue(value);
            return;
        }

        _environmentVariables.Add(new EnvironmentVariable(Id, name, value));
    }

    public void RemoveEnvironmentVariable(string name)
    {
        var existing = _environmentVariables.FirstOrDefault(e => e.Name == name);
        if (existing is not null)
        {
            _environmentVariables.Remove(existing);
        }
    }
}

public class EnvironmentVariable : Entity
{
    public Guid AppId { get; private init; }
    public string Name { get; private set; } = default!;
    public string Value { get; private set; } = default!;

    protected EnvironmentVariable() { } // EF Core

    internal EnvironmentVariable(Guid appId, string name, string value)
    {
        AppId = appId;
        Name = name;
        Value = value;
    }

    internal void UpdateValue(string value)
    {
        Value = value;
    }
}
