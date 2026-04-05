namespace Collabhost.Api.Supervisor.Containment;

public interface IContainmentHandle : IDisposable
{
    bool AssignProcess(int processId);

    void Terminate(uint exitCode);
}
