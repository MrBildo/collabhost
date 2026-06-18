using System.Reflection;

namespace Collabhost.Api.Tests.Architecture;

// Shared scaffold for the architecture arch-tests, the executable form of
// the code-structure-conventions spec. Three surfaces:
//   AllApiTypes        walks the Api assembly's type graph, anchored on IAssemblyMarker.
//   RelativeToApiRoot  strips the Api root namespace and skips foreign namespaces.
//   ApiProjectDirectory is the on-disk Collabhost.Api source root, for source-text rules.
internal static class ArchitectureTestHelpers
{
    private const string _apiRootNamespace = "Collabhost.Api.";

    private static readonly Type[] _cachedApiTypes = LoadApiTypes();

    // The Api assembly anchored on the in-assembly marker, no hard-coded name string.
    internal static IReadOnlyList<Type> AllApiTypes() => _cachedApiTypes;

    // Strip the Api root namespace from a namespace string, returning the part
    // below the assembly root. "Collabhost.Api.Supervisor.Containment" becomes
    // "Supervisor.Containment". Returns null for any namespace NOT under the Api
    // root, so the source-generated namespaces emitted into this assembly are
    // skipped: Microsoft.AspNetCore.OpenApi.Generated from the OpenAPI generator,
    // System.Text.RegularExpressions.Generated from the regex generator, and
    // System.Runtime.CompilerServices. Probe-confirmed as the foreign namespaces
    // the type graph surfaces. Skipping them is load-bearing: an unstripped foreign
    // namespace would false-flag the section-4 two-segment sub-folder check.
    internal static string? RelativeToApiRoot(string? @namespace) =>
        @namespace is not null && @namespace.StartsWith(_apiRootNamespace, StringComparison.Ordinal)
            ? @namespace[_apiRootNamespace.Length..]
            : null;

    // The on-disk Collabhost.Api source directory, resolved by walking up from the
    // test assembly's base directory to the backend solution root, the folder holding
    // Collabhost.slnx, then descending into Collabhost.Api. Used only by source-text
    // rules such as section-2a region/banner absence; the type-graph rules need no
    // filesystem access at all.
    internal static string ApiProjectDirectory { get; } = ResolveApiProjectDirectory();

    private static Type[] LoadApiTypes()
    {
        try
        {
            return typeof(IAssemblyMarker).Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return [.. ex.Types.Where(t => t is not null)!];
        }
    }

    private static string ResolveApiProjectDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Collabhost.slnx")))
            {
                var apiDir = Path.Combine(current.FullName, "Collabhost.Api");

                if (Directory.Exists(apiDir))
                {
                    return apiDir;
                }
            }

            current = current.Parent;
        }

        throw new InvalidOperationException
        (
            "Could not locate the Collabhost.Api source directory by walking up from "
                + AppContext.BaseDirectory
                + " to the folder containing Collabhost.slnx."
        );
    }
}
