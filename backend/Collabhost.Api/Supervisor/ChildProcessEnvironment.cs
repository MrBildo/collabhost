namespace Collabhost.Api.Supervisor;

// Builds the COMPLETE environment a hosted child process is launched with.
//
// The supervisor (ProcessSupervisor.MergeEnvironmentVariables) already composes
// the full *intended* child environment from scratch -- an empty dictionary
// filled by the capability/operator-override tier, the IProcessEnvironmentProvider
// tier, then port injection. That curated dictionary is the told-input contract:
// it is exactly what the operator, the app type, and the platform decided the
// child should see, and nothing else.
//
// The runners (Linux / Windows / Fallback) used to splice that curated
// dictionary on top of the supervisor's OWN process environment -- either by
// mutating the .NET-pre-seeded ProcessStartInfo.EnvironmentVariables (which is
// pre-populated with the parent process env) or by merging it with
// Environment.GetEnvironmentVariables(). That made every hosted child inherit
// Collabhost's own host-scoped vars -- ASPNETCORE_CONTENTROOT,
// ASPNETCORE_ENVIRONMENT, DOTNET_ENVIRONMENT, COLLABHOST_* -- which the systemd
// unit / Windows service sets for Collabhost ITSELF. A hosted ASP.NET app then
// resolved ContentRoot to Collabhost's install dir, missed its own
// appsettings*.json, and aborted (#330).
//
// The fix is the model, not a denylist: the child environment is a *told input*,
// built from the curated dictionary plus a minimal, explicit allowlist of
// OS-context variables a process genuinely needs to start (PATH, the locale,
// the platform loader essentials). Collabhost's own ASP.NET / DOTNET /
// COLLABHOST identity variables are NOT on the allowlist -- they reach a child
// only if the operator or the app type set them via the environment-defaults
// capability, in which case they are in the curated dictionary already and are
// honored. This is an allowlist (enumerate what is safe), never a denylist
// (enumerate what is dangerous and hope the list is complete).
//
// Used by all three runners so Linux, Windows, and the fallback are consistent
// by construction. Stateless static helper -- same shape as
// HostedDotnetBundleEnvironment, no DI.
internal static class ChildProcessEnvironment
{
    // OS-context variables a spawned process genuinely needs to start, that the
    // curated supervisor dictionary does not carry. These are the operating
    // system's contract with any process it runs -- not Collabhost's identity.
    //
    // Deliberately excluded because they are Collabhost-host identity rather than
    // OS context, and reach a child only via the environment-defaults
    // capability: the ASP.NET host vars, the dotnet environment selector, the
    // single-file bundle-extract dir, and the Collabhost path vars -- the #330
    // leak set. The bundle-extract dir in particular is provisioned per-app into
    // the capability tier by HostedDotnetBundleEnvironment for self-extracting
    // single-file apps (#313 / #322 decision 3); inheriting Collabhost's own
    // value here would defeat that gate.

    private static readonly string[] _linuxAllowlist =
    [
        "PATH",        // resolve `dotnet`, `node`, and the app's own deps
        "HOME",        // libc / some runtimes derive paths from it
        "USER",        // a few tools read it; harmless OS context
        "LOGNAME",     // ditto
        "LANG",        // .NET globalization reads the locale from here
        "LC_ALL",
        "LC_CTYPE",
        "TZ",          // timezone -- OS context, not Collabhost identity
        "TMPDIR"       // POSIX temp dir; PrivateTmp-isolated under systemd
    ];

    private static readonly string[] _windowsAllowlist =
    [
        "SystemRoot",            // Win32 processes fail to start without it
        "windir",                // alias many tools expect alongside SystemRoot
        "SystemDrive",
        "PATH",                  // resolve dotnet / node / app deps
        "PATHEXT",               // command resolution honors this
        "ComSpec",               // cmd-spawning child processes need it
        "TEMP",                  // per-process temp; isolated by the service acct
        "TMP",
        "USERPROFILE",           // runtime / SDK derive paths from it
        "ProgramData",           // machine-wide app data root
        "ProgramFiles",
        "ProgramFiles(x86)",
        "CommonProgramFiles",
        "NUMBER_OF_PROCESSORS",  // some runtimes size thread pools from it
        "PROCESSOR_ARCHITECTURE"
    ];

    // Builds the full child environment: every OS-context allowlisted variable
    // present in the supervisor's own process environment, then the curated
    // supervisor dictionary applied ON TOP (so a curated key always wins over an
    // accidentally-same-named OS var -- the curated value is the told input).
    //
    // Returns a fresh dictionary the caller owns. Ordinal keying matches the
    // rest of the supervisor's env handling (MergeEnvironmentVariables).
    public static Dictionary<string, string> Build
    (
        IReadOnlyDictionary<string, string> curated
    )
    {
        ArgumentNullException.ThrowIfNull(curated);

        var allowlist = OperatingSystem.IsWindows()
            ? _windowsAllowlist
            : _linuxAllowlist;

        // Windows environment-variable names are case-insensitive; POSIX names
        // are case-sensitive. Match the OS so an allowlist entry resolves
        // regardless of the host's casing on Windows.
        var keyComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        var result = new Dictionary<string, string>(keyComparer);

        foreach (var key in allowlist)
        {
            var value = Environment.GetEnvironmentVariable(key);

            if (value is not null)
            {
                result[key] = value;
            }
        }

        foreach (var (key, value) in curated)
        {
            result[key] = value;
        }

        return result;
    }

    // Applies the full child environment onto a ProcessStartInfo whose
    // EnvironmentVariables dictionary is .NET-pre-seeded with the PARENT
    // (supervisor) process environment. The pre-seed is the #330 leak vector,
    // so it is CLEARED first, then the curated + allowlisted set is applied.
    // Used by the Linux and Fallback runners and the Windows Process.Start
    // fallback path.
    public static void Apply
    (
        System.Collections.Specialized.StringDictionary target,
        IReadOnlyDictionary<string, string> curated
    )
    {
        ArgumentNullException.ThrowIfNull(target);

        // Clear the inherited supervisor environment. Without this the child
        // sees Collabhost's own ASPNETCORE_CONTENTROOT / COLLABHOST_* / etc.
        target.Clear();

        foreach (var (key, value) in Build(curated))
        {
            target[key] = value;
        }
    }
}
