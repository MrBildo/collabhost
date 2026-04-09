using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Collabhost.Api.Supervisor;

[SupportedOSPlatform("linux")]
internal static partial class LinuxNativeMethods
{
    public const int SIGTERM = 15;
    public const int SIGKILL = 9;
    public const int ESRCH = 3;
    public const int EACCES = 13;

    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    public static partial int Kill(int pid, int sig);

    [LibraryImport("libc", EntryPoint = "setpgid", SetLastError = true)]
    public static partial int SetProcessGroupId(int pid, int pgid);

    [LibraryImport("libc", EntryPoint = "getpgid", SetLastError = true)]
    public static partial int GetProcessGroupId(int pid);
}
