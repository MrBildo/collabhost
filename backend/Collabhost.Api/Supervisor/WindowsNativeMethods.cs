using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Win32.SafeHandles;

namespace Collabhost.Api.Supervisor;

[SupportedOSPlatform("windows")]
internal static class WindowsNativeMethods
{
    // Process creation flags
    public const uint CreateNewProcessGroup = 0x00000200;
    public const uint CreateNewConsole = 0x00000010;
    public const uint CreateUnicodeEnvironment = 0x00000400;

    // Console control events
    public const uint CtrlBreakEvent = 1;

    // STARTUPINFO flags
    public const uint StartFUseStdHandles = 0x00000100;
    public const uint StartFUseShowWindow = 0x00000001;

    // ShowWindow values
    public const ushort SwHide = 0;

    // Handle flags for SetHandleInformation
    public const uint HandleFlagInherit = 0x00000001;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CreateProcess
    (
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref StartupInfo lpStartupInfo,
        out ProcessInformation lpProcessInformation
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CreatePipe
    (
        out SafeFileHandle hReadPipe,
        out SafeFileHandle hWritePipe,
        ref SecurityAttributes lpPipeAttributes,
        uint nSize
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetHandleInformation
    (
        SafeFileHandle hObject,
        uint dwMask,
        uint dwFlags
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct StartupInfo
    {
        public uint Cb;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public uint X;
        public uint Y;
        public uint XSize;
        public uint YSize;
        public uint XCountChars;
        public uint YCountChars;
        public uint FillAttribute;
        public uint Flags;
        public ushort ShowWindow;
        public ushort Reserved2Size;
        public IntPtr Reserved2;

        // Raw IntPtr instead of SafeFileHandle because P/Invoke marshaling
        // throws ArgumentNullException when a SafeFileHandle field is null.
        // The STARTUPINFO struct does not own these handles -- they are
        // borrowed from the pipes and must NOT be closed via this struct.
        public IntPtr StdInput;
        public IntPtr StdOutput;
        public IntPtr StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SecurityAttributes
    {
        public uint Length;
        public IntPtr SecurityDescriptor;

        [MarshalAs(UnmanagedType.Bool)]
        public bool InheritHandle;
    }
}
