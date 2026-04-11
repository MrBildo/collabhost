using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

using Collabhost.Api.Shared;

using Microsoft.Win32.SafeHandles;

namespace Collabhost.Api.Supervisor;

public class WindowsProcessRunner(ILogger<WindowsProcessRunner> logger) : IManagedProcessRunner
{
    private readonly ILogger<WindowsProcessRunner> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    public IProcessHandle Start(ProcessStartConfiguration configuration)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                return StartWithCreateProcess(configuration);
            }
            catch (Exception exception)
            {
                _logger.LogWarning
                (
                    exception,
                    "CreateProcess P/Invoke failed -- falling back to Process.Start()"
                );
            }
        }

        return StartWithDotNetProcess(configuration);
    }

    public async Task<ProcessRunResult> RunToCompletionAsync
    (
        ProcessStartConfiguration configuration,
        TimeSpan timeout,
        CancellationToken ct = default
    )
    {
        // RunToCompletion uses standard Process.Start -- it is for short-lived commands
        // (e.g., update scripts) where graceful shutdown is not needed.
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

    [SupportedOSPlatform("windows")]
    private ProcessGroupHandle StartWithCreateProcess(ProcessStartConfiguration configuration)
    {
        var resolvedCommand = ResolveCommand(configuration.Command);
        var commandLine = BuildCommandLine(resolvedCommand, configuration.Arguments);
        var environmentBlock = BuildEnvironmentBlock(configuration.EnvironmentVariables);

        // Create stdout pipe
        var stdoutSecurity = new WindowsNativeMethods.SecurityAttributes
        {
            Length = (uint)Marshal.SizeOf<WindowsNativeMethods.SecurityAttributes>(),
            SecurityDescriptor = IntPtr.Zero,
            InheritHandle = 1
        };

        if (!WindowsNativeMethods.CreatePipe(out var stdoutRead, out var stdoutWrite, ref stdoutSecurity, 0))
        {
            var stdoutError = Marshal.GetLastPInvokeError();

            throw new InvalidOperationException(
                string.Format(CultureInfo.InvariantCulture, "Failed to create stdout pipe (error: {0})", stdoutError));
        }

        // Prevent the read end from being inherited by the child
        WindowsNativeMethods.SetHandleInformation(stdoutRead, WindowsNativeMethods.HandleFlagInherit, 0);

        // Create stderr pipe
        var stderrSecurity = new WindowsNativeMethods.SecurityAttributes
        {
            Length = (uint)Marshal.SizeOf<WindowsNativeMethods.SecurityAttributes>(),
            SecurityDescriptor = IntPtr.Zero,
            InheritHandle = 1
        };

        if (!WindowsNativeMethods.CreatePipe(out var stderrRead, out var stderrWrite, ref stderrSecurity, 0))
        {
            stdoutRead.Dispose();
            stdoutWrite.Dispose();

            var stderrError = Marshal.GetLastPInvokeError();

            throw new InvalidOperationException(
                string.Format(CultureInfo.InvariantCulture, "Failed to create stderr pipe (error: {0})", stderrError));
        }

        // Prevent the read end from being inherited by the child
        WindowsNativeMethods.SetHandleInformation(stderrRead, WindowsNativeMethods.HandleFlagInherit, 0);

        // DangerousGetHandle is required because STARTUPINFO expects raw HANDLE values.
        // The SafeFileHandles remain alive until after CreateProcess completes and the
        // write ends are explicitly disposed below -- no risk of use-after-close.
#pragma warning disable S3869
        var startupInfo = new WindowsNativeMethods.StartupInfo
        {
            Cb = (uint)Marshal.SizeOf<WindowsNativeMethods.StartupInfo>(),
            // STARTF_USESTDHANDLES: redirect stdout/stderr through our pipes
            // STARTF_USESHOWWINDOW: honor ShowWindow to hide the console window
            Flags = WindowsNativeMethods.StartFUseStdHandles | WindowsNativeMethods.StartFUseShowWindow,
            ShowWindow = WindowsNativeMethods.SwHide,
            StdInput = IntPtr.Zero,
            StdOutput = stdoutWrite.DangerousGetHandle(),
            StdError = stderrWrite.DangerousGetHandle()
        };
#pragma warning restore S3869

        // CREATE_NEW_PROCESS_GROUP: child gets its own process group (group ID = its PID).
        //   This is required so GenerateConsoleCtrlEvent(CTRL_BREAK_EVENT, groupId) targets
        //   only the child tree, not the Collabhost host process.
        // CREATE_NEW_CONSOLE: child gets its own console (hidden via SW_HIDE above).
        //   Without a console, GenerateConsoleCtrlEvent has no target to deliver the event to.
        //   CREATE_NO_WINDOW would prevent console allocation entirely, making Ctrl+Break a no-op.
        var creationFlags = WindowsNativeMethods.CreateNewProcessGroup | WindowsNativeMethods.CreateNewConsole;

        var environmentPointer = IntPtr.Zero;

        if (environmentBlock is not null)
        {
            environmentPointer = Marshal.StringToHGlobalUni(environmentBlock);
            creationFlags |= WindowsNativeMethods.CreateUnicodeEnvironment;
        }

        try
        {
            if (!WindowsNativeMethods.CreateProcess
            (
                null,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                true,
                creationFlags,
                environmentPointer,
                configuration.WorkingDirectory,
                ref startupInfo,
                out var processInformation
            ))
            {
                var error = Marshal.GetLastPInvokeError();

                stdoutRead.Dispose();
                stdoutWrite.Dispose();
                stderrRead.Dispose();
                stderrWrite.Dispose();

                throw new InvalidOperationException(
                    string.Format(CultureInfo.InvariantCulture, "CreateProcess failed for '{0}' (error: {1})", resolvedCommand, error));
            }

            // Close the thread handle immediately -- we don't need it
            WindowsNativeMethods.CloseHandle(processInformation.Thread);

            // Close the write ends of the pipes -- the child has inherited copies.
            // If we keep ours open, reads on the pipe will never see EOF.
            stdoutWrite.Dispose();
            stderrWrite.Dispose();

            // Wrap the raw process handle into a System.Diagnostics.Process so the rest
            // of the supervisor infrastructure (WaitForExitAsync, Kill, etc.) continues
            // to work with a Process object.
            //
            // Race condition analysis (GetProcessById vs. fast child exit):
            // GetProcessById internally calls OpenProcess, which succeeds as long as the
            // process object exists in the kernel. On Windows, a process object is not
            // destroyed until ALL handles to it are closed. We still hold the CreateProcess
            // handle (processInformation.Process) at this point, so the kernel object is
            // guaranteed to exist -- even if the child exited in sub-millisecond time.
            // This also prevents PID recycling until we close our handle below.
            var process = Process.GetProcessById((int)processInformation.ProcessId);
            process.EnableRaisingEvents = true;

            // Close the raw process handle from CreateProcess -- Process.GetProcessById
            // opened its own handle internally, so the kernel object stays alive.
            WindowsNativeMethods.CloseHandle(processInformation.Process);

            var processGroupId = processInformation.ProcessId;

            var handle = new ProcessGroupHandle(process, processGroupId, configuration.OnOutput, _logger);

            // Start async pipe readers
            handle.StartPipeReaders(stdoutRead, stderrRead);

            _logger.LogDebug
            (
                "Started process via CreateProcess (PID {Pid}, ProcessGroupId {GroupId})",
                process.Id,
                processGroupId
            );

            return handle;
        }
        finally
        {
            if (environmentPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(environmentPointer);
            }
        }
    }

    private DotNetProcessHandle StartWithDotNetProcess(ProcessStartConfiguration configuration)
    {
        var startInfo = CreateStartInfo(configuration);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var handle = new DotNetProcessHandle(process, configuration.OnOutput);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return handle;
    }

    private static string BuildCommandLine(string command, string? arguments)
    {
        // CreateProcess requires a single command line string. If the command contains
        // spaces it must be quoted.
        var quoted = command.Contains(' ', StringComparison.Ordinal) ? $"\"{command}\"" : command;

        return string.IsNullOrEmpty(arguments) ? quoted : $"{quoted} {arguments}";
    }

    private static string? BuildEnvironmentBlock(IReadOnlyDictionary<string, string> environmentVariables)
    {
        if (environmentVariables.Count == 0)
        {
            return null;
        }

        // CreateProcess with CREATE_UNICODE_ENVIRONMENT expects:
        // KEY=VALUE\0KEY=VALUE\0\0
        // We must include ALL environment variables (current + overrides),
        // because passing a non-null environment block replaces the entire environment.
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in Environment.GetEnvironmentVariables())
        {
            if (entry is System.Collections.DictionaryEntry de
                && de.Key is string key
                && de.Value is string value)
            {
                merged[key] = value;
            }
        }

        foreach (var (key, value) in environmentVariables)
        {
            merged[key] = value;
        }

        var builder = new StringBuilder();

        foreach (var (key, value) in merged.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(key);
            builder.Append('=');
            builder.Append(value);
            builder.Append('\0');
        }

        builder.Append('\0');

        return builder.ToString();
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

    // No subclasses expected -- process handle created via CreateProcess with
    // CREATE_NEW_PROCESS_GROUP, enabling graceful shutdown via CTRL_BREAK_EVENT
    [SupportedOSPlatform("windows")]
    private sealed class ProcessGroupHandle : IProcessHandle
    {
        private readonly Process _process;
        private readonly uint _processGroupId;
        private readonly Action<string, LogStream> _onOutput;
        private readonly ILogger _logger;
        private CancellationTokenSource? _pipeReaderCancellation;
        private SafeFileHandle? _stdoutReadHandle;
        private SafeFileHandle? _stderrReadHandle;

        public ProcessGroupHandle
        (
            Process process,
            uint processGroupId,
            Action<string, LogStream> onOutput,
            ILogger logger
        )
        {
            _process = process;
            _processGroupId = processGroupId;
            _onOutput = onOutput;
            _logger = logger;

            _process.Exited += (_, _) =>
            {
                // Cancel pipe readers when the process exits so they don't block indefinitely
                _pipeReaderCancellation?.Cancel();

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

            // CTRL_BREAK_EVENT targets the process group. Because we started the
            // child with CREATE_NEW_PROCESS_GROUP, its group ID equals its PID and
            // is distinct from the Collabhost host's group. The event is delivered
            // only to processes in the target group -- the host is not affected.
            if (!WindowsNativeMethods.GenerateConsoleCtrlEvent(WindowsNativeMethods.CtrlBreakEvent, _processGroupId))
            {
                var error = Marshal.GetLastPInvokeError();

                _logger.LogWarning
                (
                    "GenerateConsoleCtrlEvent failed for PID {Pid} / group {GroupId} (error: {Error})",
                    _process.Id,
                    _processGroupId,
                    error
                );

                return false;
            }

            _logger.LogDebug
            (
                "Sent CTRL_BREAK_EVENT to process group {GroupId} (PID {Pid})",
                _processGroupId,
                _process.Id
            );

            return true;
        }

        public void Kill()
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }

        public void StartPipeReaders(SafeFileHandle stdoutRead, SafeFileHandle stderrRead)
        {
            // Store references so Dispose() can clean up even if FileStream
            // construction fails inside ReadPipeAsync. FileStream.Dispose is
            // idempotent with SafeFileHandle.Dispose, so double-dispose is safe.
            _stdoutReadHandle = stdoutRead;
            _stderrReadHandle = stderrRead;

            _pipeReaderCancellation = new CancellationTokenSource();

            var ct = _pipeReaderCancellation.Token;

            // Pipe readers are intentionally fire-and-forget. They are self-contained
            // with full error handling and are cancelled when the process exits.
#pragma warning disable VSTHRD110, MA0134, CS4014, CA2016, MA0040
            Task.Run(() => ReadPipeAsync(stdoutRead, LogStream.StdOut, ct), ct);
            Task.Run(() => ReadPipeAsync(stderrRead, LogStream.StdErr, ct), ct);
#pragma warning restore VSTHRD110, MA0134, CS4014, CA2016, MA0040
        }

        private async Task ReadPipeAsync(SafeFileHandle pipeHandle, LogStream stream, CancellationToken ct)
        {
            try
            {
                // Anonymous pipes from CreatePipe are not opened for overlapped I/O,
                // so isAsync must be false. The method is already running on a thread pool
                // thread (Task.Run), so synchronous reads are acceptable.
                await using var fileStream = new FileStream(pipeHandle, FileAccess.Read, bufferSize: 4096, isAsync: false);
                using var reader = new StreamReader(fileStream, Encoding.UTF8);

                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);

                    if (line is null)
                    {
                        break;
                    }

                    _onOutput(line, stream);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when the process exits and the pipe reader is cancelled
            }
            catch (Exception exception)
            {
                _logger.LogDebug
                (
                    exception,
                    "Pipe reader ({Stream}) ended for PID {Pid}",
                    stream,
                    _process.Id
                );
            }
        }

        public void Dispose()
        {
            try
            {
                _pipeReaderCancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // CTS may have been disposed by a concurrent Exited event callback
            }

            _pipeReaderCancellation?.Dispose();
            _stdoutReadHandle?.Dispose();
            _stderrReadHandle?.Dispose();
            _process.Dispose();
        }
    }

    // No subclasses expected -- fallback process handle using standard .NET Process.Start
    private sealed class DotNetProcessHandle : IProcessHandle
    {
        private readonly Process _process;

        public DotNetProcessHandle(Process process, Action<string, LogStream> onOutput)
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

        // WM_CLOSE works for apps with a message loop (e.g., GUI apps).
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
