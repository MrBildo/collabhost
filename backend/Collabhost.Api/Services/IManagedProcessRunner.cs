namespace Collabhost.Api.Services;

public interface IManagedProcessRunner
{
    IProcessHandle Start(ProcessStartConfig config);

    Task<ProcessRunResult> RunToCompletionAsync(ProcessStartConfig config, TimeSpan timeout, CancellationToken ct = default);
}

public record ProcessRunResult(int ExitCode, bool TimedOut);

public record ProcessStartConfig
(
    string Command,
    string? Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    Action<string, LogStream> OnOutput
);

public interface IProcessHandle : IDisposable
{
    int Pid { get; }
    bool HasExited { get; }
    int? ExitCode { get; }
    event Action<int>? Exited;
    void Kill();
}
