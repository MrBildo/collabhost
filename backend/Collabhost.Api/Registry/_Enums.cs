namespace Collabhost.Api.Registry;

public enum ProcessState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Crashed,
    Restarting,
    Backoff,
    Fatal
}

public enum RestartPolicy
{
    Never,
    OnCrash,
    Always
}

public enum ServeMode
{
    ReverseProxy,
    FileServer
}

public enum DiscoveryStrategy
{
    DotNetRuntimeConfiguration,
    DotNetProject,
    PackageJson,
    Manual
}

#pragma warning disable MA0076
public static class DiscoveryStrategyExtensions
{
    extension(DiscoveryStrategy strategy)
    {
        public string ToCamelCase()
        {
            var name = strategy.ToString();

            return char.ToLowerInvariant(name[0]) + name[1..];
        }
    }
}
#pragma warning restore MA0076
