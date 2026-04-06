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
