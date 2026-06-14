using System.Reflection;
using System.Runtime.CompilerServices;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Architecture;

// Section 11 (F5) of the code-structure-conventions spec. Registration is central and
// explicit: every subsystem with a DI or endpoint surface owns one _Registration.cs
// exposing its Add<Subsystem>() / Map<Subsystem>() extension. The invariant enforced here
// is FILE-PRESENCE -- does each surface-bearing subsystem folder contain a _Registration.cs?
//
// The detector is extension-block-aware. A C# 14 extension block
//   extension(IServiceCollection services) { public IServiceCollection AddX() { ... } }
// lowers (probe-confirmed, not assumed) to a public static method on the declaring class
// carrying [System.Runtime.CompilerServices.ExtensionAttribute] with the receiver type as
// its first positional parameter. Keying on [Extension] + first-parameter-type matches both
// the extension-block and the legacy this-parameter source shapes, so it is robust to either.
//
// Scoped to Collabhost.Api.*: RelativeToApiRoot returns null for any namespace outside the
// assembly's own root, so source-generated [Extension] registrations emitted into the
// assembly -- Microsoft.AspNetCore.OpenApi.Generated's AddOpenApi from builder.Services
// .AddOpenApi() -- are excluded and never reach the file-presence assertion.
//
// MUTATION-PROOFING. A file-presence test can pass vacuously two ways, and both fail loud
// here. (1) The detector is guarded: the surface-bearing set must equal the expected count of
// distinct surface-bearing subsystem namespaces -- if the extension-block detector silently
// matches zero (the exact hazard a legacy this-shape predicate creates), the count guard goes
// RED rather than the file-presence test passing on an empty set. (2) The assertion has been
// seen RED: renaming any surface-bearing folder's _Registration.cs to a non-_Registration name
// re-reds the file-presence fact with an actionable message (verified during authoring -- this
// test is RED on the current tree until Mcp/_McpRegistration.cs is renamed to _Registration.cs,
// the one prefix exception section 11 closes).
public class RegistrationFilePresenceTests
{
    // The receiver types that mark an extension method as a DI or endpoint registration
    // surface. A first-parameter type in this set + [Extension] is the registration shape.
    // Compared against the reflected ParameterType.Name, so simple type-name literals (no
    // namespace) are the right shape and avoid taking a dependency on the ASP.NET types here.
    private static readonly string[] _registrationReceiverTypes =
    [
        "IServiceCollection",
        "IEndpointRouteBuilder",
        "WebApplication"
    ];

    // The count of distinct surface-bearing subsystem namespaces (grouped by subsystem-root
    // segment). 16, not the 17 surface-bearing folders: two folders fold into an existing
    // namespace bucket. System/'s registration surface declares namespace Collabhost.Api
    // .Platform (folds into Platform, adding no distinct root), and Data/AppTypes's namespace
    // is Data.AppTypes whose subsystem-root segment is Data (folds into the Data bucket).
    // Counting folders yields 17; grouping by subsystem-root namespace yields 16.
    private const int _expectedSurfaceBearingSubsystemCount = 16;

    // Mutation-proof reading 1: guard the detector. If the extension-block detector silently
    // matches zero, this is the test that goes RED rather than file-presence passing vacuously.
    [Fact]
    public void Surface_bearing_subsystem_detector_finds_the_expected_count()
    {
        var subsystems = SurfaceBearingSubsystemRootSegments();

        subsystems.ShouldNotBeEmpty
        (
            "§11 detector matched zero registration surfaces -- the extension-block detector is "
                + "broken (the legacy this-shape hazard). File-presence would pass vacuously."
        );

        subsystems.Length.ShouldBe
        (
            _expectedSurfaceBearingSubsystemCount,
            "§11 expected "
                + _expectedSurfaceBearingSubsystemCount
                + " distinct surface-bearing subsystem namespaces, found "
                + subsystems.Length
                + ": "
                + string.Join(", ", subsystems)
        );
    }

    // The invariant: every surface-bearing subsystem folder contains a _Registration.cs.
    [Fact]
    public void Every_surface_bearing_subsystem_owns_a_registration_file()
    {
        var apiRoot = ArchitectureTestHelpers.ApiProjectDirectory;

        var offenders = SurfaceBearingSubsystemRootSegments()
            .Where(segment => !File.Exists(Path.Combine(apiRoot, segment, "_Registration.cs")))
            .Order(StringComparer.Ordinal)
            .ToArray();

        offenders.ShouldBeEmpty
        (
            "§11 requires every subsystem with a DI or endpoint surface to own a _Registration.cs. "
                + "These surface-bearing subsystems have no _Registration.cs in their folder "
                + "(a registration file is misnamed or missing): "
                + string.Join(", ", offenders)
        );
    }

    // Step 1 of the §11 predicate: detect the surface-bearing subsystems, extension-block-aware
    // and scoped to Collabhost.Api.*. Returns the distinct subsystem-root segments (the first
    // namespace segment below the Api root), which is the grouping key the count guard counts
    // and the folder file-presence resolves against.
    private static string[] SurfaceBearingSubsystemRootSegments() =>
    [
        .. ArchitectureTestHelpers.AllApiTypes()
            .SelectMany(DeclaredExtensionMethods)
            .Where(IsRegistrationSurface)
            .Select(method => ArchitectureTestHelpers.RelativeToApiRoot(method.DeclaringType?.Namespace))
            .Where(relative => relative is not null)
            .Select(relative => relative!.Split('.')[0])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
    ];

    private static IEnumerable<MethodInfo> DeclaredExtensionMethods(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttribute<ExtensionAttribute>() is not null);

    private static bool IsRegistrationSurface(MethodInfo method)
    {
        var parameters = method.GetParameters();

        return parameters.Length > 0
            && _registrationReceiverTypes.Contains(parameters[0].ParameterType.Name, StringComparer.Ordinal);
    }
}
