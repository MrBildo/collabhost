using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Registry;

namespace Collabhost.Api.Supervisor;

// Decides whether a hosted dotnet-app's runtime cwd should be redirected to a
// platform-provisioned per-app writable directory, and -- when it should --
// performs the redirect on the discovered process WITHOUT breaking dotnet's
// dll/runtimeconfig resolution. (#316 / Axis B.)
//
// The load-bearing constraint (recon §(g)): DiscoverDotNetApplication returns
// a BARE dll filename as the argument, which `dotnet` resolves against the
// child's cwd. If the cwd moves off the artifact location without absolutizing
// that argument, `dotnet` can no longer find the dll and *.runtimeconfig.json.
// So this redirect keeps the ARTIFACT LOCATION as the discovery anchor (the
// caller still discovers there, so runtimeconfig/dll are found) and only the
// LAUNCHED working directory moves -- with the dll argument rewritten to an
// absolute path under the artifact location so it still resolves once cwd is
// elsewhere.
//
// Why gated to DotNetRuntimeConfiguration specifically (not every dotnet-app
// strategy): only the published-app `dotnet <dll>` shape has a single,
// cleanly-absolutizable artifact-relative argument. `DotNetProject`
// (`dotnet run --project <csproj>`) and `Manual` either resolve their inputs
// against cwd in ways that are not a one-arg rewrite, or are explicit operator
// configuration that already owns the working directory. Narrowing to the
// single-file/published shape -- which is the only shape #316's bug actually
// exhibits, and the shape a hosted production app like Collaboard uses -- keeps
// the blast radius to exactly the broken case: apps that work today (cwd =
// artifact dir, which they can write to) are untouched; apps broken today
// (relative writes under a read-only artifact cwd) get the writable cwd.
//
// Why not an IProcessEnvironmentProvider / capability tier: this is not an env
// var -- it rewrites the process working directory and an argument. It is the
// cwd analogue of CH-C's bundle dir, applied at the same StartAppInternalAsync
// seam, gated symmetrically (dotnet-app + operator has not pinned a working
// directory -> explicit operator intent wins silently).
internal static class HostedDotnetWorkingDirectoryRedirect
{
    // The literal app-type slug; matched directly with StringComparison.Ordinal
    // here for consistency with HostedDotnetBundleEnvironment / ProbeCurator.
    public const string DotnetAppTypeSlug = "dotnet-app";

    // True when the supervisor should redirect this app's runtime cwd to a
    // platform-provisioned per-app dir. Gated to:
    //   - app type dotnet-app, AND
    //   - the published-app discovery shape (DotNetRuntimeConfiguration), AND
    //   - the operator has NOT pinned a process WorkingDirectory.
    //
    // operatorPinned reports the working-dir pin independently so the caller
    // can reason about the escape-hatch path. An operator-pinned working
    // directory is an explicit "run with cwd = <this>" instruction and wins
    // silently -- symmetric to CH-C's operator-override-wins posture and
    // CLAUDE.md Repository Rule 3 (no regression for anything working today).
    public static bool ShouldRedirect
    (
        string appTypeSlug,
        ProcessConfiguration processConfiguration,
        out bool operatorPinned
    )
    {
        ArgumentNullException.ThrowIfNull(processConfiguration);

        operatorPinned = !string.IsNullOrWhiteSpace(processConfiguration.WorkingDirectory);

        return string.Equals(appTypeSlug, DotnetAppTypeSlug, StringComparison.Ordinal)
            && processConfiguration.DiscoveryStrategy == DiscoveryStrategy.DotNetRuntimeConfiguration
            && !operatorPinned;
    }

    // Rewrites a discovered process so it launches with cwd = the
    // platform-provisioned writable directory while keeping dotnet's dll
    // resolution anchored at the artifact location.
    //
    // discoveredProcess was produced by DiscoverDotNetApplication against the
    // artifact location, so:
    //   - Command   == "dotnet"
    //   - Arguments == "<AppName>.dll" (bare filename, cwd-relative)
    //   - WorkingDirectory == the artifact location
    //
    // The rewrite absolutizes the dll argument against the (still-artifact)
    // working directory, then swaps the working directory for the per-app
    // writable one. Any provider-augmented suffix on the arguments (e.g. a
    // port flag) is preserved -- only the leading bare dll token is
    // absolutized.
    public static DiscoveredProcess Redirect
    (
        DiscoveredProcess discoveredProcess,
        string writableWorkingDirectory
    )
    {
        ArgumentNullException.ThrowIfNull(discoveredProcess);
        ArgumentException.ThrowIfNullOrWhiteSpace(writableWorkingDirectory);

        var absoluteArguments = AbsolutizeLeadingDllArgument
        (
            discoveredProcess.Arguments,
            discoveredProcess.WorkingDirectory
        );

        return discoveredProcess with
        {
            Arguments = absoluteArguments,
            WorkingDirectory = writableWorkingDirectory
        };
    }

    // The arguments string from DiscoverDotNetApplication is the bare dll name,
    // optionally followed by provider-augmented tokens. Absolutize ONLY the
    // first token (the dll path) against the artifact directory; leave the
    // remainder verbatim. A null/empty arguments string is returned unchanged
    // (defensive -- DiscoverDotNetApplication always produces the dll name, so
    // this is the never-expected path).
    private static string? AbsolutizeLeadingDllArgument(string? arguments, string artifactDirectory)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return arguments;
        }

        var firstSpace = arguments.IndexOf(' ', StringComparison.Ordinal);

        var dllToken = firstSpace < 0 ? arguments : arguments[..firstSpace];
        var remainder = firstSpace < 0 ? string.Empty : arguments[firstSpace..];

        var absoluteDll = Path.GetFullPath(Path.Combine(artifactDirectory, dllToken));

        return absoluteDll + remainder;
    }
}
