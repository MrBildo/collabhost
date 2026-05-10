using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;

namespace Collabhost.Api.Supervisor.Containment;

[SupportedOSPlatform("linux")]
public class LinuxContainment : IProcessContainment, IDisposable
{
    private readonly bool _cgroupAvailable;
    private readonly string? _cgroupBasePath;
    private readonly bool _setprivAvailable;
    private readonly ILogger<LinuxContainment> _logger;

    public LinuxContainment(ILogger<LinuxContainment> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        (_cgroupAvailable, _cgroupBasePath) = ProbeCgroupV2();
        _setprivAvailable = ProbeSetpriv();

        if (_cgroupAvailable)
        {
            _logger.LogInformation
            (
                "Linux process containment: cgroup v2 available at '{BasePath}'",
                _cgroupBasePath
            );

            if (_setprivAvailable)
            {
                _logger.LogInformation
                (
                    "Linux orphan-kill watcher: setpriv --pdeathsig available -- " +
                    "managed cgroups will be killed atomically when the supervisor exits abruptly"
                );
            }
            else
            {
                _logger.LogWarning
                (
                    "Linux orphan-kill watcher: setpriv --pdeathsig not available. " +
                    "Graceful shutdown still kills cgroups; abrupt supervisor termination " +
                    "(SIGKILL) will leave managed processes as orphans until the next boot's " +
                    "stale-cgroup sweep"
                );
            }

            CleanupStaleCgroups();
        }
        else
        {
            _logger.LogInformation
            (
                "Linux process containment unavailable: no writable cgroup v2. " +
                "Managed processes will not have orphan protection"
            );
        }
    }

    public IContainmentHandle? CreateContainer(string name)
    {
        if (!_cgroupAvailable || _cgroupBasePath is null)
        {
            return null;
        }

        var cgroupPath = Path.Combine(_cgroupBasePath, "collabhost", name);

        try
        {
            Directory.CreateDirectory(cgroupPath);

            _logger.LogDebug("Created cgroup container at '{Path}'", cgroupPath);

            var watcher = _setprivAvailable
                ? StartOrphanKillWatcher(cgroupPath)
                : null;

            return new CgroupContainmentHandle(cgroupPath, watcher, _logger);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning
            (
                exception,
                "Failed to create cgroup directory '{Path}'",
                cgroupPath
            );

            return null;
        }
    }

    private Process? StartOrphanKillWatcher(string cgroupPath)
    {
        // Spawn a tiny bash watcher whose only job is to write "1" to cgroup.kill
        // when its parent (this process, the supervisor) dies abruptly.
        //
        // Mechanism:
        //   1. setpriv --pdeathsig SIGTERM -- the kernel sends SIGTERM to this process
        //      as soon as its immediate parent dies, including on parent SIGKILL.
        //   2. The bash trap on SIGTERM writes "1" to <cgroup>/cgroup.kill, which
        //      atomically SIGKILLs every member of the cgroup (workload + descendants).
        //   3. The watcher itself joins the cgroup via cgroup.procs, so the cgroup.kill
        //      write also kills the watcher -- no leftover process.
        //
        // On graceful shutdown, the supervisor calls IContainmentHandle.Terminate(),
        // which writes cgroup.kill directly. The watcher receives SIGKILL (not SIGTERM)
        // along with the workload; its trap does not fire and is not needed. The
        // watcher is purely an abrupt-death contingency.
        var killPath = Path.Combine(cgroupPath, "cgroup.kill");
        var procsPath = Path.Combine(cgroupPath, "cgroup.procs");

        // Bash script: trap SIGTERM, write to cgroup.kill, then sleep forever.
        // Critical: bash signal handlers do not preempt foreground commands. A bare
        // `sleep 86400` would block bash from reading the signal until the sleep
        // returns. Backgrounding `sleep` and `wait`-ing on it makes bash interrupt
        // the wait when the signal arrives, fire the trap immediately, and exit.
        // The trap writes "1" to cgroup.kill which atomically SIGKILLs every member
        // of the cgroup -- including this bash, the workload, and all descendants.
        var script = $"trap 'echo 1 > {killPath}; exit 0' SIGTERM; while :; do sleep 86400 & wait $!; done";

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "setpriv",
                ArgumentList =
                {
                    "--pdeathsig",
                    "SIGTERM",
                    "bash",
                    "-c",
                    script
                },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var watcher = Process.Start(startInfo);

            if (watcher is null)
            {
                _logger.LogWarning("Failed to start orphan-kill watcher for '{Path}'", cgroupPath);

                return null;
            }

            // Drain stdout/stderr so the redirected pipes do not fill and block bash.
            // The watcher should never emit output, but a stuck pipe would deadlock the trap.
            watcher.OutputDataReceived += (_, _) => { };
            watcher.ErrorDataReceived += (_, _) => { };
            watcher.BeginOutputReadLine();
            watcher.BeginErrorReadLine();

            // Move the watcher into the workload's cgroup so cgroup.kill catches it too.
            try
            {
                File.WriteAllText
                (
                    procsPath,
                    watcher.Id.ToString(CultureInfo.InvariantCulture)
                );

                _logger.LogDebug
                (
                    "Started orphan-kill watcher (PID {Pid}) for cgroup '{Path}'",
                    watcher.Id,
                    cgroupPath
                );

                return watcher;
            }
            catch (IOException exception)
            {
                _logger.LogWarning
                (
                    exception,
                    "Failed to assign watcher PID {Pid} to cgroup '{Path}' -- killing watcher",
                    watcher.Id,
                    cgroupPath
                );

                try
                {
                    if (!watcher.HasExited)
                    {
                        LinuxNativeMethods.Kill(watcher.Id, LinuxNativeMethods.SIGKILL);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Process already exited
                }

                watcher.Dispose();

                return null;
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _logger.LogWarning
            (
                exception,
                "Failed to launch orphan-kill watcher for '{Path}'",
                cgroupPath
            );

            return null;
        }
    }

    public bool IsSupported(ContainmentCapability capability) =>
        capability == ContainmentCapability.KillOnClose && _cgroupAvailable;

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        // Clean up the collabhost parent directory if empty.
        // Individual cgroup handles clean up their own directories on Dispose.
        if (_cgroupBasePath is null)
        {
            return;
        }

        var collabhostDir = Path.Combine(_cgroupBasePath, "collabhost");

        try
        {
            if (Directory.Exists(collabhostDir) && Directory.GetDirectories(collabhostDir).Length == 0)
            {
                Directory.Delete(collabhostDir);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup -- not critical if the parent directory lingers
        }
    }

    private void CleanupStaleCgroups()
    {
        if (_cgroupBasePath is null)
        {
            return;
        }

        var collabhostDir = Path.Combine(_cgroupBasePath, "collabhost");

        if (!Directory.Exists(collabhostDir))
        {
            return;
        }

        foreach (var childDir in Directory.GetDirectories(collabhostDir))
        {
            var procsPath = Path.Combine(childDir, "cgroup.procs");

            try
            {
                if (File.Exists(procsPath))
                {
                    var pids = File.ReadAllText(procsPath)
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries);

                    foreach (var pidStr in pids)
                    {
                        if (int.TryParse(pidStr, CultureInfo.InvariantCulture, out var pid))
                        {
                            _logger.LogWarning
                            (
                                "Killing stale PID {Pid} in orphaned cgroup '{Path}'",
                                pid,
                                childDir
                            );

                            LinuxNativeMethods.Kill(pid, LinuxNativeMethods.SIGKILL);
                        }
                    }
                }

                // Remove the stale cgroup directory. rmdir succeeds only when the cgroup
                // has no member processes -- the kernel enforces this.
                Directory.Delete(childDir);

                _logger.LogInformation("Cleaned up stale cgroup directory '{Path}'", childDir);
            }
            catch (IOException exception)
            {
                _logger.LogWarning
                (
                    exception,
                    "Could not clean up stale cgroup '{Path}' (processes may still be exiting)",
                    childDir
                );
            }
        }
    }

    private static bool ProbeSetpriv()
    {
        // setpriv(1) from util-linux is required for the orphan-kill watcher.
        // It supports --pdeathsig which calls prctl(PR_SET_PDEATHSIG, ...) before
        // exec'ing the wrapped command. This is present on virtually every Linux
        // distribution including minimal containers and WSL2 (same util-linux
        // package as setsid).
        try
        {
            using var probe = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "setpriv",
                    Arguments = "--help",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            probe.Start();
            probe.WaitForExit(TimeSpan.FromSeconds(5));

            // Confirm the --pdeathsig option is available (older util-linux may lack it).
            var helpText = probe.StandardOutput.ReadToEnd() + probe.StandardError.ReadToEnd();

            return helpText.Contains("--pdeathsig", StringComparison.Ordinal);
        }
        catch
        {
            // File not found, permission denied, etc.
            return false;
        }
    }

    private static (bool Available, string? BasePath) ProbeCgroupV2()
    {
        // cgroup v2 unified hierarchy indicator
        if (!File.Exists("/sys/fs/cgroup/cgroup.controllers"))
        {
            return (false, null);
        }

        var selfCgroup = GetSelfCgroupPath();

        if (selfCgroup is null)
        {
            return (false, null);
        }

        // Verify write access by creating and removing a probe directory
        var testDir = Path.Combine(selfCgroup, "collabhost", ".probe");

        try
        {
            Directory.CreateDirectory(testDir);
            Directory.Delete(testDir);

            return (true, selfCgroup);
        }
        catch (UnauthorizedAccessException)
        {
            return (false, null);
        }
        catch (IOException)
        {
            return (false, null);
        }
    }

    private static string? GetSelfCgroupPath()
    {
        // /proc/self/cgroup contains "0::<path>" for cgroup v2 unified hierarchy.
        // The "0::" prefix indicates the unified hierarchy (v2). On cgroup v1 systems,
        // lines start with a non-zero hierarchy ID.
        try
        {
            var content = File.ReadAllText("/proc/self/cgroup").Trim();

            // cgroup v2: single line "0::<relative-path>"
            foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("0::", StringComparison.Ordinal))
                {
                    var relativePath = line["0::".Length..].Trim();

                    return Path.Combine("/sys/fs/cgroup", relativePath.TrimStart('/'));
                }
            }
        }
        catch (IOException)
        {
            // /proc/self/cgroup not readable
        }

        return null;
    }

    // No subclasses expected -- cgroup v2 filesystem-based containment handle
    private sealed class CgroupContainmentHandle(string cgroupPath, Process? watcher, ILogger logger)
        : IContainmentHandle
    {
        private readonly string _cgroupPath = cgroupPath;
        private readonly Process? _watcher = watcher;
        private readonly ILogger _logger = logger;

        public bool AssignProcess(int processId)
        {
            try
            {
                File.WriteAllText
                (
                    Path.Combine(_cgroupPath, "cgroup.procs"),
                    processId.ToString(CultureInfo.InvariantCulture)
                );

                return true;
            }
            catch (IOException exception)
            {
                _logger.LogWarning
                (
                    exception,
                    "Failed to assign PID {Pid} to cgroup '{Path}'",
                    processId,
                    _cgroupPath
                );

                return false;
            }
        }

        public void Terminate(uint exitCode)
        {
            // The exitCode parameter is Windows-specific -- it is passed to
            // TerminateJobObject(handle, exitCode) to set the exit code of all terminated
            // processes. On Linux, cgroup.kill sends SIGKILL to all processes in the cgroup.
            // The exit code of a SIGKILL'd process is kernel-determined (128 + signal = 137).
            // This parameter is ignored.
            var killPath = Path.Combine(_cgroupPath, "cgroup.kill");

            if (File.Exists(killPath))
            {
                try
                {
                    // cgroup.kill (Linux 5.14+): atomically sends SIGKILL to all processes
                    // in the cgroup. This is the cgroup v2 equivalent of TerminateJobObject.
                    File.WriteAllText(killPath, "1");

                    return;
                }
                catch (IOException exception)
                {
                    _logger.LogWarning
                    (
                        exception,
                        "cgroup.kill failed for '{Path}', falling back to manual kill",
                        _cgroupPath
                    );
                }
            }

            // Fallback for kernels < 5.14: read cgroup.procs and SIGKILL each PID
            KillAllMembers();
        }

        public void Dispose()
        {
            // The watcher is in the cgroup. If Terminate() was called, cgroup.kill
            // already SIGKILL'd it. If Dispose is called without Terminate (e.g. the
            // workload exited cleanly on its own and only the watcher remains),
            // explicitly kill the watcher so the cgroup empties.
            //
            // We must wait for the kernel to fully reap the watcher before attempting
            // rmdir -- the cgroup keeps the PID listed in cgroup.procs until the
            // process is reaped, and rmdir on a non-empty cgroup returns EBUSY.
            if (_watcher is not null)
            {
                try
                {
                    if (!_watcher.HasExited)
                    {
                        LinuxNativeMethods.Kill(_watcher.Id, LinuxNativeMethods.SIGKILL);

                        // WaitForExit blocks until the kernel has reaped the process.
                        // 5s is a generous bound for a SIGKILL'd bash on idle WSL2.
                        _watcher.WaitForExit(5000);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Process already exited / handle disposed
                }

                _watcher.Dispose();
            }

            try
            {
                // rmdir succeeds only when the cgroup has no member processes --
                // the kernel enforces this constraint
                Directory.Delete(_cgroupPath);
            }
            catch (IOException exception)
            {
                _logger.LogDebug
                (
                    exception,
                    "Could not remove cgroup directory '{Path}' (processes may still be exiting)",
                    _cgroupPath
                );
            }
        }

        private void KillAllMembers()
        {
            var procsPath = Path.Combine(_cgroupPath, "cgroup.procs");

            try
            {
                var pids = File.ReadAllText(procsPath)
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries);

                foreach (var pidStr in pids)
                {
                    if (int.TryParse(pidStr, CultureInfo.InvariantCulture, out var pid))
                    {
                        LinuxNativeMethods.Kill(pid, LinuxNativeMethods.SIGKILL);
                    }
                }
            }
            catch (IOException exception)
            {
                _logger.LogWarning
                (
                    exception,
                    "Failed to read cgroup.procs for manual kill at '{Path}'",
                    _cgroupPath
                );
            }
        }
    }
}
