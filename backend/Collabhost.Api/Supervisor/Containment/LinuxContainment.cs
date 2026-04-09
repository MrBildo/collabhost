using System.Globalization;
using System.Runtime.Versioning;

namespace Collabhost.Api.Supervisor.Containment;

[SupportedOSPlatform("linux")]
public class LinuxContainment : IProcessContainment, IDisposable
{
    private readonly bool _cgroupAvailable;
    private readonly string? _cgroupBasePath;
    private readonly ILogger<LinuxContainment> _logger;

    public LinuxContainment(ILogger<LinuxContainment> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        (_cgroupAvailable, _cgroupBasePath) = ProbeCgroupV2();

        if (_cgroupAvailable)
        {
            _logger.LogInformation
            (
                "Linux process containment: cgroup v2 available at '{BasePath}'",
                _cgroupBasePath
            );

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

            return new CgroupContainmentHandle(cgroupPath, _logger);
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
        var collabhostDir = Path.Combine(_cgroupBasePath!, "collabhost");

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
    private sealed class CgroupContainmentHandle(string cgroupPath, ILogger logger)
        : IContainmentHandle
    {
        private readonly string _cgroupPath = cgroupPath;
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
