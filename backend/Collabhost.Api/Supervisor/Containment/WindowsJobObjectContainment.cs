using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Win32.SafeHandles;

namespace Collabhost.Api.Supervisor.Containment;

[SupportedOSPlatform("windows")]
public partial class WindowsJobObjectContainment(ILogger<WindowsJobObjectContainment> logger) : IProcessContainment
{
    private readonly ILogger<WindowsJobObjectContainment> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    private readonly bool _isHostInJob = DetectHostJobMembership(logger);

    public IContainmentHandle? CreateContainer(string name)
    {
        if (_isHostInJob)
        {
            return null;
        }

        var jobName = $"collabhost-{name}";

        var securityAttributes = new SecurityAttributes
        {
            Length = (uint)Marshal.SizeOf<SecurityAttributes>(),
            SecurityDescriptor = IntPtr.Zero,

            // CRITICAL: bInheritHandle MUST be false. If children inherit the job handle,
            // closing our handle is not the "last close" and kill-on-close does not fire.
            InheritHandle = 0
        };

        var jobHandle = NativeMethods.CreateJobObject(ref securityAttributes, jobName);

        if (jobHandle.IsInvalid)
        {
            _logger.LogWarning
            (
                "Failed to create job object '{JobName}' (error: {Error})",
                jobName,
                Marshal.GetLastPInvokeError()
            );

            return null;
        }

        var extendedInfo = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = NativeMethods.JobObjectLimitKillOnJobClose
            }
        };

        var size = (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>();

        if (!NativeMethods.SetInformationJobObject(jobHandle, JobObjectInfoType.ExtendedLimitInformation, ref extendedInfo, size))
        {
            _logger.LogWarning
            (
                "Failed to set kill-on-close for job '{JobName}' (error: {Error})",
                jobName,
                Marshal.GetLastPInvokeError()
            );

            jobHandle.Dispose();

            return null;
        }

        _logger.LogDebug("Created job object '{JobName}'", jobName);

        return new JobObjectHandle(jobHandle, jobName, _logger);
    }

    public bool IsSupported(ContainmentCapability capability) =>
        capability == ContainmentCapability.KillOnClose && !_isHostInJob;

    private static bool DetectHostJobMembership(ILogger logger)
    {
        try
        {
            var currentProcess = NativeMethods.GetCurrentProcess();

            if (!NativeMethods.IsProcessInJob(currentProcess, IntPtr.Zero, out var isInJob) || !isInJob)
            {
                return false;
            }

            // Host is in a job. On Windows 10+, nested jobs work by default -- they only
            // fail when the parent job restricts it (e.g., Aspire's DCP). Probe by spawning
            // a short-lived child process and attempting to assign it to a temporary job.
            // This tests the exact operation we will perform in production: child processes
            // inherit the parent's job membership, and we need to assign them to our own job.
            //
            // Previous approach tested with the current process's pseudo-handle, which has
            // special semantics and can succeed even when child assignment would fail.
            return !ProbeChildJobAssignment(logger);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to detect host job object membership");
        }

        return false;
    }

    private static bool ProbeChildJobAssignment(ILogger logger)
    {
        var securityAttributes = new SecurityAttributes
        {
            Length = (uint)Marshal.SizeOf<SecurityAttributes>(),
            SecurityDescriptor = IntPtr.Zero,
            InheritHandle = 0
        };

        using var probeJob = NativeMethods.CreateJobObject(ref securityAttributes, null);

        if (probeJob.IsInvalid)
        {
            logger.LogInformation
            (
                "Process containment unavailable: could not create probe job object " +
                "(typical under Aspire/Visual Studio). Managed processes will not have orphan protection"
            );

            return false;
        }

        // Spawn a short-lived child process to test job assignment. The child inherits
        // the parent's job membership (just like managed app processes will).
        try
        {
            using var probe = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c exit 0",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            probe.Start();

            var probeHandle = NativeMethods.OpenProcess
            (
                NativeMethods.ProcessAssignProcess,
                false,
                (uint)probe.Id
            );

            if (probeHandle == IntPtr.Zero)
            {
                logger.LogInformation
                (
                    "Process containment unavailable: could not open probe child process " +
                    "(typical under Aspire/Visual Studio). Managed processes will not have orphan protection"
                );

                probe.Kill();

                return false;
            }

            try
            {
                if (!NativeMethods.AssignProcessToJobObject(probeJob, probeHandle))
                {
                    logger.LogInformation
                    (
                        "Process containment unavailable: child process job assignment blocked by parent job " +
                        "(typical under Aspire/Visual Studio). Managed processes will not have orphan protection"
                    );

                    return false;
                }

                // Child process successfully assigned to probe job -- containment will work.
                return true;
            }
            finally
            {
                NativeMethods.CloseHandle(probeHandle);

                if (!probe.HasExited)
                {
                    probe.Kill();
                }
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning
            (
                exception,
                "Process containment probe failed -- assuming containment unavailable"
            );

            return false;
        }
    }

    // No subclasses expected -- private kernel handle wrapper
    private sealed class SafeJobHandle() : SafeHandleZeroOrMinusOneIsInvalid(ownsHandle: true)
    {
        protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
    }

    // No subclasses expected -- private per-process containment handle
    private sealed class JobObjectHandle(SafeJobHandle jobHandle, string jobName, ILogger logger)
        : IContainmentHandle
    {
        private readonly SafeJobHandle _jobHandle = jobHandle;
        private readonly string _jobName = jobName;
        private readonly ILogger _logger = logger;

        public bool AssignProcess(int processId)
        {
            var processHandle = NativeMethods.OpenProcess(NativeMethods.ProcessAssignProcess, false, (uint)processId);

            if (processHandle == IntPtr.Zero)
            {
                _logger.LogWarning
                (
                    "Failed to open process {ProcessId} for job assignment '{JobName}' (error: {Error})",
                    processId,
                    _jobName,
                    Marshal.GetLastPInvokeError()
                );

                return false;
            }

            try
            {
                if (!NativeMethods.AssignProcessToJobObject(_jobHandle, processHandle))
                {
                    _logger.LogWarning
                    (
                        "Failed to assign process {ProcessId} to job '{JobName}' (error: {Error})",
                        processId,
                        _jobName,
                        Marshal.GetLastPInvokeError()
                    );

                    return false;
                }

                _logger.LogDebug("Assigned process {ProcessId} to job '{JobName}'", processId, _jobName);

                return true;
            }
            finally
            {
                NativeMethods.CloseHandle(processHandle);
            }
        }

        public void Terminate(uint exitCode)
        {
            if (!_jobHandle.IsInvalid && !_jobHandle.IsClosed)
            {
                NativeMethods.TerminateJobObject(_jobHandle, exitCode);
            }
        }

        public void Dispose() => _jobHandle.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public uint Length;
        public IntPtr SecurityDescriptor;
        public int InheritHandle;
    }

    private enum JobObjectInfoType
    {
        ExtendedLimitInformation = 9
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    private static partial class NativeMethods
    {
        public const uint JobObjectLimitKillOnJobClose = 0x2000;

        // AssignProcessToJobObject requires PROCESS_SET_QUOTA | PROCESS_TERMINATE
        public const uint ProcessSetQuota = 0x0100;
        public const uint ProcessTerminate = 0x0001;
        public const uint ProcessAssignProcess = ProcessSetQuota | ProcessTerminate;

        [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        public static partial SafeJobHandle CreateJobObject(ref SecurityAttributes lpJobAttributes, [MarshalAs(UnmanagedType.LPWStr)] string? lpName);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetInformationJobObject
        (
            SafeJobHandle hJob,
            JobObjectInfoType jobObjectInformationClass,
            ref JobObjectExtendedLimitInformation lpJobObjectInformation,
            uint cbJobObjectInformationLength
        );

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool AssignProcessToJobObject(SafeJobHandle hJob, IntPtr hProcess);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool TerminateJobObject(SafeJobHandle hJob, uint uExitCode);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        public static partial IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CloseHandle(IntPtr hObject);

        [LibraryImport("kernel32.dll")]
        public static partial IntPtr GetCurrentProcess();

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool IsProcessInJob(
            IntPtr processHandle,
            IntPtr jobHandle,
            [MarshalAs(UnmanagedType.Bool)] out bool result);
    }
}
