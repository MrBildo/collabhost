namespace Collabhost.Api.Domain.Capabilities;

public sealed class ProcessConfiguration
{
    public string DiscoveryStrategy { get; set; } = default!;

    public bool GracefulShutdown { get; set; }

    public int ShutdownTimeoutSeconds { get; set; }

    public string? Command { get; set; }

    public string? Arguments { get; set; }

    public string? WorkingDirectory { get; set; }
}

public sealed class PortInjectionConfiguration
{
    public string EnvironmentVariableName { get; set; } = default!;

    public string PortFormat { get; set; } = default!;
}

public sealed class RoutingConfiguration
{
    public string DomainPattern { get; set; } = default!;

    public string ServeMode { get; set; } = default!;

    public bool? SpaFallback { get; set; }
}

public sealed class HealthCheckConfiguration
{
    public string Endpoint { get; set; } = default!;

    public int IntervalSeconds { get; set; }

    public int TimeoutSeconds { get; set; }

    public int? Retries { get; set; }
}

public sealed class EnvironmentDefaultsConfiguration
{
    public IDictionary<string, string> Defaults { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

public sealed class RestartConfiguration
{
    public string Policy { get; set; } = default!;
}

public sealed class AutoStartConfiguration
{
    public bool Enabled { get; set; }
}

public sealed class AspNetRuntimeConfiguration
{
    public string TargetFramework { get; set; } = default!;

    public string RuntimeVersion { get; set; } = default!;

    public bool SelfContained { get; set; }
}

public sealed class NodeRuntimeConfiguration
{
    public string NodeVersion { get; set; } = default!;

    public string PackageManager { get; set; } = default!;

    public string? BuildCommand { get; set; }
}

public sealed class ReactRuntimeConfiguration
{
    public string ReactVersion { get; set; } = default!;

    public string? Router { get; set; }

    public string? Bundler { get; set; }
}

public sealed class ArtifactConfiguration
{
    public string Location { get; set; } = default!;
}
