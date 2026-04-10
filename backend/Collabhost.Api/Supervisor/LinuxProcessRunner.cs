using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Collabhost.Api.Shared;

namespace Collabhost.Api.Supervisor;

[SupportedOSPlatform("linux")]
public class LinuxProcessRunner : IManagedProcessRunner
{
    private readonly ILogger<LinuxProcessRunner> _logger;
    private readonly bool _setsidAvailable;

    public LinuxProcessRunner(ILogger<LinuxProcessRunner> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _setsidAvailable = ProbeSetsid();

        if (_setsidAvailable)
        {
            _logger.LogInformation("setsid command available -- process group establishment will be race-free");
        }
        else
        {
            _logger.LogWarning
            (
                "setsid command not found -- falling back to post-fork setpgid " +
                "(process group establishment may race with child exec)"
            );
        }
    }

    public IProcessHandle Start(ProcessStartConfiguration configuration)
    {
        var startInfo = CreateStartInfo(configuration, _setsidAvailable);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.Start();

        var childPid = process.Id;
        var processGroupEstablished = _setsidAvailable;

        // Defense-in-depth: attempt setpgid from the parent regardless of setsid.
        //
        // When setsid is available, the child has already called setsid() before exec,
        // establishing its own session and process group atomically. The parent's setpgid
        // call will likely fail with EACCES (child already exec'd) or EPERM (child is a
        // session leader). Both are harmless -- setsid already did the work.
        //
        // When setsid is NOT available, this is the primary mechanism. The race with
        // exec still applies: if the child exec's before this call, setpgid returns
        // EACCES and we fall back to single-PID signals.
        var setpgidResult = LinuxNativeMethods.SetProcessGroupId(childPid, childPid);

        if (setpgidResult != 0)
        {
            var errno = Marshal.GetLastPInvokeError();

            if (_setsidAvailable)
            {
                // Expected when using setsid -- the child is already a session leader,
                // so parent-side setpgid fails. This is the happy path.
                _logger.LogDebug
                (
                    "setpgid({Pid}, {Pgid}) failed (errno {Errno}) -- expected with setsid wrapper",
                    childPid,
                    childPid,
                    errno
                );
            }
            else
            {
                // Without setsid, setpgid failure means we lost the race.
                // Fall back to single-PID signals.
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
        }

        // Create the handle BEFORE starting the output read pump. The handle constructor
        // wires OutputDataReceived/ErrorDataReceived event handlers. If BeginOutputReadLine
        // is called first, fast-exiting processes (e.g., `echo`) can produce output before
        // the handler is registered, causing the output to be silently dropped.
        var handle = new LinuxProcessGroupHandle
        (
            process,
            childPid,
            processGroupEstablished,
            configuration.OnOutput,
            _logger
        );

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        _logger.LogDebug
        (
            "Started process via {LaunchMethod} (PID {Pid}, GroupEstablished {GroupEstablished})",
            _setsidAvailable ? "setsid" : "Process.Start",
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
        // RunToCompletion uses standard Process.Start without setsid -- it is for
        // short-lived commands (e.g., update scripts) where process group signals are
        // not needed. It uses Process.Kill(entireProcessTree: true) for cleanup.
        var startInfo = CreateStartInfo(configuration, useSetsid: false);

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

    private static ProcessStartInfo CreateStartInfo
    (
        ProcessStartConfiguration configuration,
        bool useSetsid
    )
    {
        // When setsid is available, wrap the command so the child calls setsid()
        // before exec. This establishes a new session and process group atomically,
        // eliminating the race between parent-side setpgid and child exec.
        //
        // Before: FileName = "dotnet", Arguments = "run --project MyApp"
        // After:  FileName = "setsid", Arguments = "dotnet run --project MyApp"
        //
        // setsid(1) from util-linux calls setsid() then execvp(argv[1], argv+1),
        // transparently passing through all arguments and inherited file descriptors
        // (including the redirected stdio pipes from Process.Start).
        var fileName = useSetsid ? "setsid" : configuration.Command;

        var arguments = useSetsid
            ? BuildSetsidArguments(configuration.Command, configuration.Arguments)
            : configuration.Arguments ?? "";

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
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

    private static string BuildSetsidArguments(string command, string? arguments) =>
        string.IsNullOrEmpty(arguments)
            ? command
            : $"{command} {arguments}";

    private static bool ProbeSetsid()
    {
        // Check for the setsid command from util-linux. This is present on virtually
        // every Linux distribution including minimal containers and WSL2.
        // We probe once at construction (DI singleton) rather than per-launch.
        try
        {
            using var probe = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "setsid",
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            probe.Start();
            probe.WaitForExit(TimeSpan.FromSeconds(5));

            // setsid --version exits 0 on util-linux implementations.
            // Any successful start means the binary exists.
            return true;
        }
        catch
        {
            // File not found, permission denied, etc.
            return false;
        }
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
