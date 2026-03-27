using Collabhost.Api.Services;

namespace Collabhost.Api.Tests.Fixtures;

public class FakeProcessRunner : IManagedProcessRunner
{
    public IProcessHandle Start(ProcessStartConfig config)
    {
        return new FakeProcessHandle();
    }
}

public class FakeProcessHandle : IProcessHandle
{
    private static int _nextPid = 10000;

    public int Pid { get; } = Interlocked.Increment(ref _nextPid);
    public bool HasExited { get; private set; }
    public int? ExitCode { get; private set; }
#pragma warning disable CS0067 // Event required by interface, intentionally unused in fake
    public event Action<int>? Exited;
#pragma warning restore CS0067

    public void Kill()
    {
        if (!HasExited)
        {
            HasExited = true;
            ExitCode = -1;
        }
    }

    public void Dispose() { }
}
