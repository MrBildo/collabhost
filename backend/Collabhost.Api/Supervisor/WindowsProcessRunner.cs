using System.Diagnostics;
using System.Runtime.InteropServices;

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

        private bool TryGracefulShutdownWindows()
        {
            if (_process.CloseMainWindow())
            {
                return true;
            }

            // For console apps started with CreateNoWindow=true + redirected I/O,
            // CloseMainWindow() returns false. Use GenerateConsoleCtrlEvent as fallback.
            try
            {
                FreeConsole();

                if (!AttachConsole((uint)_process.Id))
                {
                    AttachConsole(_attachParentProcess);
                    return false;
                }

                SetConsoleCtrlHandler(null, true);

                var sent = GenerateConsoleCtrlEvent(_ctrlCEvent, 0);

                FreeConsole();
                AttachConsole(_attachParentProcess);
                SetConsoleCtrlHandler(null, false);

                return sent;
            }
            catch
            {
                FreeConsole();
                AttachConsole(_attachParentProcess);
                SetConsoleCtrlHandler(null, false);
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
