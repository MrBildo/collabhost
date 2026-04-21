using System.Reflection;
using System.Runtime.InteropServices;

namespace Collabhost.Api.Platform;

public static class VersionInfo
{
    private static readonly Lazy<string> _version = new(Compute);

    private static readonly Lazy<string> _commit = new(ComputeCommit);

    public static string Current => _version.Value;

    public static string Commit => _commit.Value;

    public static string Platform => RuntimeInformation.RuntimeIdentifier;

    public static string StripCommitHash(string raw)
    {
        var plusIndex = raw.IndexOf('+', StringComparison.Ordinal);
        return plusIndex >= 0 ? raw[..plusIndex] : raw;
    }

    private static string Compute()
    {
        var raw = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";

        return StripCommitHash(raw);
    }

    private static string ComputeCommit()
    {
        var raw = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "";

        var plusIndex = raw.IndexOf('+', StringComparison.Ordinal);
        return plusIndex >= 0 ? raw[(plusIndex + 1)..] : "";
    }
}
