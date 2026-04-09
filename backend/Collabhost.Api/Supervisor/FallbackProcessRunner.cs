using System.Diagnostics;
using System.Runtime.InteropServices;

using Collabhost.Api.Shared;

namespace Collabhost.Api.Supervisor;

public class FallbackProcessRunner : IManagedProcessRunner
{
    private readonly ILogger<FallbackProcessRunner> _logger;

    public FallbackProcessRunner(ILogger<FallbackProcessRunner> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var operatingSystem = RuntimeInformation.OSDescription;

        _logger.LogWarning
        (
            "Running on {OperatingSystem} with degraded process management " +
            "-- graceful shutdown and orphan protection are not available",
            operatingSystem
        );
    }

    public IProcessHandle Start(ProcessStartConfiguration configuration)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = configuration.Command,
            Arguments = configuration.Arguments ?? "",
            WorkingDirectory = configuration.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var (key, value) in configuration.EnvironmentVariables)
        {
            startInfo.EnvironmentVariables[key] = value;
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var handle = new FallbackProcessHandle(process, configuration.OnOutput);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        _logger.LogDebug("Started process via fallback runner (PID {Pid})", process.Id);

        return handle;
    }

    public async Task<ProcessRunResult> RunToCompletionAsync
    (
        ProcessStartConfiguration configuration,
        TimeSpan timeout,
        CancellationToken ct = default
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = configuration.Command,
            Arguments = configuration.Arguments ?? "",
            WorkingDirectory = configuration.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var (key, value) in configuration.EnvironmentVariables)
        {
            startInfo.EnvironmentVariables[key] = value;
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                configuration.OnOutput(e.Data, LogStream.StdOut);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                configuration.OnOutput(e.Data, LogStream.StdErr);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCancellation.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCancellation.Token);

            return new ProcessRunResult(process.ExitCode, false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            return new ProcessRunResult(-1, true);
        }
    }

    // No subclasses expected -- fallback process handle using standard .NET Process.Start.
    // Graceful shutdown via CloseMainWindow is ineffective for console apps (returns false).
    private sealed class FallbackProcessHandle : IProcessHandle
    {
        private readonly Process _process;

        public FallbackProcessHandle(Process process, Action<string, LogStream> onOutput)
        {
            _process = process;

            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    onOutput(e.Data, LogStream.StdOut);
                }
            };

            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    onOutput(e.Data, LogStream.StdErr);
                }
            };

            _process.Exited += (_, _) =>
            {
                Exited?.Invoke(_process.ExitCode);
            };
        }

        public int Pid => _process.Id;

        public bool HasExited => _process.HasExited;

        public int? ExitCode => _process.HasExited ? _process.ExitCode : null;

        public event Action<int>? Exited;

        // WM_CLOSE / CloseMainWindow works for apps with a message loop (e.g., GUI apps).
        // For console apps started with CreateNoWindow=true, this returns false
        // and the caller falls through to hard kill.
        public bool TryGracefulShutdown() =>
            _process.HasExited || _process.CloseMainWindow();

        public void Kill()
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }

        public void Dispose() => _process.Dispose();
    }
}
