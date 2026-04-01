using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Collabhost.Api.Services;

public class WindowsProcessRunner : IManagedProcessRunner
{
    public IProcessHandle Start(ProcessStartConfig config)
    {
        var startInfo = CreateStartInfo(config);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var handle = new ProcessHandle(process, config.OnOutput);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return handle;
    }

    public async Task<ProcessRunResult> RunToCompletionAsync(ProcessStartConfig config, TimeSpan timeout, CancellationToken ct = default)
    {
        var startInfo = CreateStartInfo(config);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                config.OnOutput(e.Data, LogStream.StdOut);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                config.OnOutput(e.Data, LogStream.StdErr);
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

    private static ProcessStartInfo CreateStartInfo(ProcessStartConfig config)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = config.Command,
            Arguments = config.Arguments ?? "",
            WorkingDirectory = config.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var (key, value) in config.EnvironmentVariables)
        {
            startInfo.EnvironmentVariables[key] = value;
        }

        return startInfo;
    }

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

        public bool TryGracefulShutdown()
        {
            if (_process.HasExited)
            {
                return true;
            }

            if (OperatingSystem.IsWindows())
            {
                return TryGracefulShutdownWindows();
            }

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                return TryGracefulShutdownUnix();
            }

            return false;
        }

        private bool TryGracefulShutdownWindows()
        {
            // Try CloseMainWindow first (works for GUI apps that have a message loop)
            if (_process.CloseMainWindow())
            {
                return true;
            }

            // For console apps started with CreateNoWindow=true + redirected I/O,
            // CloseMainWindow() returns false. Use GenerateConsoleCtrlEvent as fallback.
            // We must briefly attach to the child's console group, send Ctrl+C,
            // then detach and re-ignore Ctrl+C on our own process.
            try
            {
                // Detach from our current console so we can attach to the child's
                _ = FreeConsole();

                if (!AttachConsole((uint)_process.Id))
                {
                    // Reattach to our own console if attach failed
                    _ = AttachConsole(_attachParentProcess);
                    return false;
                }

                // Disable Ctrl+C handling on our own process so we don't kill ourselves
                _ = SetConsoleCtrlHandler(null, true);

                // Send Ctrl+C to the console group (pid 0 = all processes attached to the console)
                var sent = GenerateConsoleCtrlEvent(_ctrlCEvent, 0);

                // Reattach to our own console and re-enable Ctrl+C handling
                _ = FreeConsole();
                _ = AttachConsole(_attachParentProcess);
                _ = SetConsoleCtrlHandler(null, false);

                return sent;
            }
            catch
            {
                // If P/Invoke fails for any reason, restore console state and return false
                _ = FreeConsole();
                _ = AttachConsole(_attachParentProcess);
                _ = SetConsoleCtrlHandler(null, false);
                return false;
            }
        }

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

        // P/Invoke for Windows graceful console shutdown
        private const uint _ctrlCEvent = 0;
        private const uint _attachParentProcess = 0xFFFFFFFF;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandlerDelegate? handler, [MarshalAs(UnmanagedType.Bool)] bool add);

        private delegate bool ConsoleCtrlHandlerDelegate(uint dwCtrlType);
    }
}
