using System.Diagnostics;

using Collabhost.Api.Shared;

namespace Collabhost.Api.Supervisor;

public class WindowsProcessRunner : IManagedProcessRunner
{
    public IProcessHandle Start(ProcessStartConfiguration configuration)
    {
        var startInfo = CreateStartInfo(configuration);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var handle = new ProcessHandle(process, configuration.OnOutput);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return handle;
    }

    public async Task<ProcessRunResult> RunToCompletionAsync
    (
        ProcessStartConfiguration configuration,
        TimeSpan timeout,
        CancellationToken ct = default
    )
    {
        var startInfo = CreateStartInfo(configuration);

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

    private static ProcessStartInfo CreateStartInfo(ProcessStartConfiguration configuration)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveCommand(configuration.Command),
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

        return startInfo;
    }

    // On Windows, commands like "npm" are actually "npm.cmd" batch files.
    // With UseShellExecute=false, the OS will not resolve these automatically.
    private static string ResolveCommand(string command)
    {
        if (Path.HasExtension(command))
        {
            return command;
        }

        string[] extensions = [".cmd", ".bat", ".exe"];

        foreach (var extension in extensions)
        {
            var candidate = command + extension;

            var fullPath = Environment.GetEnvironmentVariable("PATH")?
                .Split(Path.PathSeparator)
                    .Select(directory => Path.Combine(directory, candidate))
                        .FirstOrDefault(File.Exists);

            if (fullPath is not null)
            {
                return fullPath;
            }
        }

        return command;
    }

    // No subclasses expected -- private OS process wrapper
    private sealed class ProcessHandle : IProcessHandle
    {
        private readonly Process _process;

        public ProcessHandle(Process process, Action<string, LogStream> onOutput)
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

        public bool TryGracefulShutdown() =>
            _process.HasExited
            || (OperatingSystem.IsWindows()
                ? TryGracefulShutdownWindows()
                : (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                    && TryGracefulShutdownUnix());

        // WM_CLOSE works for apps with a message loop (e.g., GUI apps).
        // For console apps started with CreateNoWindow=true, this returns false
        // and the caller falls through to hard kill.
        //
        // Note: the FreeConsole/AttachConsole/GenerateConsoleCtrlEvent approach
        // is deliberately NOT used here. FreeConsole() is a process-wide operation
        // that detaches ALL threads from the console, breaking logging and health
        // checks when running under an orchestrator (Aspire, etc.). Since we start
        // all managed processes with CreateNoWindow=true (no console allocated),
        // AttachConsole(childPid) always fails anyway -- the entire Ctrl+C path
        // was a no-op that caused collateral damage.
        //
        // Proper graceful shutdown on Windows requires CREATE_NEW_PROCESS_GROUP at
        // process creation time, which is a separate piece of work.
        private bool TryGracefulShutdownWindows() => _process.CloseMainWindow();

        private bool TryGracefulShutdownUnix()
        {
            // On Linux/macOS, Process.Kill(false) sends SIGTERM (not SIGKILL)
            try
            {
                _process.Kill(entireProcessTree: false);
                return true;
            }
            catch
            {
                return false;
            }
        }

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
