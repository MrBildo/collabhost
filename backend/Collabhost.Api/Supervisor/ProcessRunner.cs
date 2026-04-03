using Collabhost.Api.Shared;

namespace Collabhost.Api.Supervisor;

public interface IProcessHandle : IDisposable
{
    int Pid { get; }

    bool HasExited { get; }

    int? ExitCode { get; }

    event Action<int>? Exited;

    bool TryGracefulShutdown();

    void Kill();
}

public interface IManagedProcessRunner
{
    IProcessHandle Start(ProcessStartConfiguration configuration);

    Task<ProcessRunResult> RunToCompletionAsync
    (
        ProcessStartConfiguration configuration,
        TimeSpan timeout,
        CancellationToken ct = default
    );
}

public record ProcessStartConfiguration
(
    string Command,
    string? Arguments,
    string? WorkingDirectory,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    Action<string, LogStream> OnOutput
);

public record ProcessRunResult(int ExitCode, bool TimedOut);
