using Collabhost.Api.Services;

namespace Collabhost.Api.Tests.Fixtures;

public class FakeProcessRunner : IManagedProcessRunner
{
    public FakeProcessHandle? LastHandle { get; private set; }

    public IProcessHandle Start(ProcessStartConfig config)
    {
        var handle = new FakeProcessHandle(config.OnOutput);
        LastHandle = handle;
        return handle;
    }
}

public class FakeProcessHandle(Action<string, LogStream>? onOutput = null) : IProcessHandle
{
    private static int _nextPid = 10000;
    private readonly Action<string, LogStream>? _onOutput = onOutput;

    public int Pid { get; } = Interlocked.Increment(ref _nextPid);
    public bool HasExited { get; private set; }
    public int? ExitCode { get; private set; }
#pragma warning disable CS0067 // Event required by interface, intentionally unused in fake
    public event Action<int>? Exited;
#pragma warning restore CS0067

    public void EmitOutput(string line, LogStream stream = LogStream.StdOut)
    {
        _onOutput?.Invoke(line, stream);
    }

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
