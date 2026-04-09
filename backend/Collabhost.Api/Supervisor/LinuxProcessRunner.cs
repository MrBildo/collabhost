using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Collabhost.Api.Shared;

namespace Collabhost.Api.Supervisor;

[SupportedOSPlatform("linux")]
public class LinuxProcessRunner(ILogger<LinuxProcessRunner> logger) : IManagedProcessRunner
{
    private readonly ILogger<LinuxProcessRunner> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    public IProcessHandle Start(ProcessStartConfiguration configuration)
    {
        var startInfo = CreateStartInfo(configuration);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.Start();

        // Set the child into its own process group immediately after Start().
        //
        // Race analysis: Unlike Windows's CREATE_NEW_PROCESS_GROUP (which is atomic --
        // the kernel assigns the process group during CreateProcess), Linux's post-fork
        // setpgid has a real race window. The child process runs in the parent's group
        // from fork() until this setpgid call succeeds. If the child has already called
        // exec() before we get here, setpgid returns EACCES and the child remains in
        // the parent's group.
        //
        // For Collabhost's use case (long-lived services, not sub-millisecond scripts),
        // this race is unlikely to matter -- we only send group signals when stopping
        // managed processes, not during startup. When setpgid does fail, we fall back to
        // single-PID signals (see _processGroupEstablished in LinuxProcessGroupHandle).
        var childPid = process.Id;
        var processGroupEstablished = true;
        var setpgidResult = LinuxNativeMethods.SetProcessGroupId(childPid, childPid);

        if (setpgidResult != 0)
        {
            var errno = Marshal.GetLastPInvokeError();

            // EACCES: child already called exec before our setpgid took effect.
            // The child is still in the parent's process group, NOT in its own group.
            // We track this so TryGracefulShutdown and Kill use single-PID signals
            // instead of group signals.
            //
            // ESRCH: process already exited.
            //
            // Both are non-fatal -- the process still runs, just without group isolation.
            processGroupEstablished = false;

            _logger.LogDebug
            (
                "setpgid({Pid}, {Pgid}) failed (errno {Errno}). " +
                "Process group not established -- will use single-PID signals",
                childPid,
                childPid,
                errno
            );
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var handle = new LinuxProcessGroupHandle
        (
            process,
            childPid,
            processGroupEstablished,
            configuration.OnOutput,
            _logger
        );

        _logger.LogDebug
        (
            "Started process via Process.Start (PID {Pid}, GroupEstablished {GroupEstablished})",
            process.Id,
            processGroupEstablished
        );

        return handle;
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

    private static ProcessStartInfo CreateStartInfo(ProcessStartConfiguration configuration)
    {
        // No ResolveCommand call -- on Linux, the shell and PATH handle command resolution.
        // FileName is passed as-is. No .cmd/.bat/.exe extension guessing needed.
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

        return startInfo;
    }

    // No subclasses expected -- process handle using POSIX process groups via setpgid,
    // enabling graceful shutdown via SIGTERM to the process group
    private sealed class LinuxProcessGroupHandle : IProcessHandle
    {
        private readonly Process _process;
        private readonly int _processGroupId;
        private readonly bool _processGroupEstablished;
        private readonly ILogger _logger;

        public LinuxProcessGroupHandle
        (
            Process process,
            int processGroupId,
            bool processGroupEstablished,
            Action<string, LogStream> onOutput,
            ILogger logger
        )
        {
            _process = process;
            _processGroupId = processGroupId;
            _processGroupEstablished = processGroupEstablished;
            _logger = logger;

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

            if (_processGroupEstablished)
            {
                // Send SIGTERM to the entire process group by negating the PGID.
                // kill(-pgid, SIGTERM) delivers the signal to every process in the group,
                // including the root process and any children it spawned (assuming they
                // haven't changed their own PGID via setpgid/setsid).
                var result = LinuxNativeMethods.Kill(-_processGroupId, LinuxNativeMethods.SIGTERM);

                if (result != 0)
                {
                    var errno = Marshal.GetLastPInvokeError();

                    // ESRCH = no such process/group. Process may have exited between
                    // the HasExited check and the kill call. Not an error.
                    if (errno == LinuxNativeMethods.ESRCH)
                    {
                        return _process.HasExited;
                    }

                    _logger.LogWarning
                    (
                        "kill(-{GroupId}, SIGTERM) failed for PID {Pid} (errno {Errno})",
                        _processGroupId,
                        _process.Id,
                        errno
                    );

                    return false;
                }
            }
            else
            {
                // Process group was not established (setpgid failed during startup).
                // Fall back to sending SIGTERM to the individual PID only.
                // This means child processes spawned by the managed app will NOT receive
                // the signal -- but it is better than sending to a nonexistent group.
                var result = LinuxNativeMethods.Kill(_process.Id, LinuxNativeMethods.SIGTERM);

                if (result != 0)
                {
                    var errno = Marshal.GetLastPInvokeError();

                    if (errno == LinuxNativeMethods.ESRCH)
                    {
                        return _process.HasExited;
                    }

                    _logger.LogWarning
                    (
                        "kill({Pid}, SIGTERM) failed (errno {Errno})",
                        _process.Id,
                        errno
                    );

                    return false;
                }
            }

            if (_processGroupEstablished)
            {
                _logger.LogDebug
                (
                    "Sent SIGTERM to process group {GroupId} (PID {Pid})",
                    _processGroupId,
                    _process.Id
                );
            }
            else
            {
                _logger.LogDebug("Sent SIGTERM to PID {Pid}", _process.Id);
            }

            return true;
        }

        public void Kill()
        {
            if (_process.HasExited)
            {
                return;
            }

            if (_processGroupEstablished)
            {
                // First attempt: SIGKILL the entire process group
                var result = LinuxNativeMethods.Kill(-_processGroupId, LinuxNativeMethods.SIGKILL);

                if (result != 0)
                {
                    // Fallback: use .NET's KillTree which walks /proc for ppid-based children
                    if (!_process.HasExited)
                    {
                        _process.Kill(entireProcessTree: true);
                    }
                }
            }
            else
            {
                // Process group not established -- use .NET's KillTree for best-effort
                // child cleanup via ppid walking
                _process.Kill(entireProcessTree: true);
            }
        }

        public void Dispose() => _process.Dispose();
    }
}
