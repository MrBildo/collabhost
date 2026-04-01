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
    string? WorkingDirectory,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    Action<string, LogStream> OnOutput
);

public interface IProcessHandle : IDisposable
{
    int Pid { get; }

    bool HasExited { get; }

    int? ExitCode { get; }

    event Action<int>? Exited;

    /// <summary>
    /// Attempts a graceful shutdown signal (Ctrl+C on Windows, SIGTERM on Linux).
    /// Returns true if the signal was sent successfully, false if it could not be delivered.
    /// The caller must still wait for the process to exit and fall back to Kill() on timeout.
    /// </summary>
    bool TryGracefulShutdown();

    void Kill();
}
