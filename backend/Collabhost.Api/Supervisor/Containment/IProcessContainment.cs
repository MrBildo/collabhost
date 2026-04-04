namespace Collabhost.Api.Supervisor.Containment;

public interface IProcessContainment
{
    IContainmentHandle? CreateContainer(string name);

    bool IsSupported(ContainmentCapability capability);
}

public enum ContainmentCapability
{
    KillOnClose,
    CpuLimit,
    MemoryLimit,
    ResourceAccounting
}
