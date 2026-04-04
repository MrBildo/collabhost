namespace Collabhost.Api.Registry;

public enum ProcessState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Crashed,
    Restarting
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
    PackageJson,
    Manual
}
