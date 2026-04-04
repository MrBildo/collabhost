namespace Collabhost.Api.Supervisor.Containment;

public class NullContainment : IProcessContainment
{
    public IContainmentHandle? CreateContainer(string name) => null;

    public bool IsSupported(ContainmentCapability capability) => false;
}
