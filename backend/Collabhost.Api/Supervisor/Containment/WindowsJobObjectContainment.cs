using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Win32.SafeHandles;

namespace Collabhost.Api.Supervisor.Containment;

[SupportedOSPlatform("windows")]
public class WindowsJobObjectContainment(ILogger<WindowsJobObjectContainment> logger) : IProcessContainment
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
            InheritHandle = false
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

            if (NativeMethods.IsProcessInJob(currentProcess, IntPtr.Zero, out var isInJob) && isInJob)
            {
                logger.LogInformation
                (
                    "Process containment unavailable: host process is already in a job object " +
                    "(typical under Aspire/Visual Studio). Managed processes will not have orphan protection"
                );

                return true;
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to detect host job object membership");
        }

        return false;
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

        [MarshalAs(UnmanagedType.Bool)]
        public bool InheritHandle;
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

    private static class NativeMethods
    {
        public const uint JobObjectLimitKillOnJobClose = 0x2000;
        public const uint ProcessAssignProcess = 0x0001;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeJobHandle CreateJobObject(ref SecurityAttributes lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetInformationJobObject
        (
            SafeJobHandle hJob,
            JobObjectInfoType jobObjectInformationClass,
            ref JobObjectExtendedLimitInformation lpJobObjectInformation,
            uint cbJobObjectInformationLength
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AssignProcessToJobObject(SafeJobHandle hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateJobObject(SafeJobHandle hJob, uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsProcessInJob(
            IntPtr processHandle,
            IntPtr jobHandle,
            [MarshalAs(UnmanagedType.Bool)] out bool result);
    }
}
