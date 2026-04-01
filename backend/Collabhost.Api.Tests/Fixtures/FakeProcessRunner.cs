using Collabhost.Api.Services;

namespace Collabhost.Api.Tests.Fixtures;

public class FakeProcessRunner : IManagedProcessRunner
{
    public FakeProcessHandle? LastHandle { get; private set; }

    public ProcessRunResult NextRunResult { get; set; } = new(0, false);
    public ProcessStartConfig? LastRunConfig { get; private set; }
    public int RunToCompletionCallCount { get; private set; }

    public IProcessHandle Start(ProcessStartConfig config)
    {
        var handle = new FakeProcessHandle(config.OnOutput);
        LastHandle = handle;
        return handle;
    }

    public Task<ProcessRunResult> RunToCompletionAsync(ProcessStartConfig config, TimeSpan timeout, CancellationToken ct = default)
    {
        LastRunConfig = config;
        RunToCompletionCallCount++;

        config.OnOutput("fake update output", LogStream.StdOut);

        return Task.FromResult(NextRunResult);
    }
}

public class FakeProcessHandle(Action<string, LogStream>? onOutput = null) : IProcessHandle
{
    private static int _nextPid = 10000;
    private readonly Action<string, LogStream>? _onOutput = onOutput;

    public int Pid { get; } = Interlocked.Increment(ref _nextPid);
    public bool HasExited { get; private set; }
    public int? ExitCode { get; private set; }
    public bool GracefulShutdownRequested { get; private set; }
    public bool SimulateGracefulShutdownSuccess { get; set; } = true;
    public bool ExitOnGracefulShutdown { get; set; } = true;
#pragma warning disable CS0067 // Event required by interface, intentionally unused in fake
    public event Action<int>? Exited;
#pragma warning restore CS0067

    public void EmitOutput(string line, LogStream stream = LogStream.StdOut) => _onOutput?.Invoke(line, stream);

    public bool TryGracefulShutdown()
    {
        GracefulShutdownRequested = true;

        if (!SimulateGracefulShutdownSuccess)
        {
            return false;
        }

        if (ExitOnGracefulShutdown && !HasExited)
        {
            HasExited = true;
            ExitCode = 0;
        }

        return true;
    }

    public void Kill()
    {
        if (!HasExited)
        {
            HasExited = true;
            ExitCode = -1;
        }
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
