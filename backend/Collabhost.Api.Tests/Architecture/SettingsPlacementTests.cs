using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Architecture;

// Section 5 (OPEN-2) of the code-structure-conventions spec. A *Settings type lives in the
// subsystem that owns or consumes it -- there is no pooled Configuration/ or Settings/ drawer.
// A knob that parameterizes the host/process itself lives in Platform/ because Platform/ owns
// it. The invariant asserted here is PLACEMENT (the spec's arch-test half): no *Settings type
// lives outside a subsystem folder (or Platform/). The "sits with a consuming type" half is
// review-tier (IOptions<T> indirection makes "consumes" not analyzer-visible) and is NOT
// asserted here, by the spec's own split.
//
// The detector keys on the closed *Settings role-suffix (§2b). It deliberately matches BOTH
// the six configuration-settings types carrying const string SectionName (AuthorizationSettings,
// ExternalTargetSettings, TypeStoreSettings, HostingSettings, PortalSettings, ProxySettings) AND
// the one *Settings-suffixed REST contract DTO (Registry/_ApiContracts.cs's AppSettings) -- the
// suffix-based placement test includes AppSettings and it passes (Registry/ is a subsystem
// folder), exactly as the spec's row-5 re-measure records: GREEN whether you count 6 config
// types or 7 suffix-matches. Scoped to Collabhost.Api.* via RelativeToApiRoot so no framework
// or source-generated *Settings type reaches the assertion.
//
// PLACEMENT RULE, machine-statable. A *Settings type satisfies placement when its namespace,
// relative to the Api root, has a non-empty subsystem-root segment (it lives inside SOME
// subsystem folder, never stranded at the bare Collabhost.Api root) and that root segment is
// not a pool-by-kind drawer name (Configuration, Settings). The forbidden-pool guard is what
// makes the test bite the actual §5 failure mode: someone gathering knobs into a
// Collabhost.Api.Configuration/ drawer is the pool-by-kind the spine forbids.
//
// MUTATION-PROOFING. A placement test passes vacuously if the detector matches zero *Settings
// types -- then "none is mis-placed" is trivially true. The count guard below asserts the
// detector finds the full *Settings set (7 today: the 6 config types + AppSettings); a
// zero-match detector reds there rather than letting placement pass on an empty set. To see the
// placement assertion RED, move any *Settings type into a Collabhost.Api.Configuration drawer
// (or to the bare Api root) and it is named as an offender.
public class SettingsPlacementTests
{
    // Pool-by-kind drawer names §5 forbids: a *Settings type may never live in one. These are
    // the gather-things-because-they-are-the-same-kind drawers the spine rejects, distinct from
    // a subsystem folder that owns the knob it holds.
    private static readonly string[] _forbiddenPoolSegments =
    [
        "Configuration",
        "Settings"
    ];

    // The count of *Settings-suffixed types in the Api assembly: the 6 configuration-settings
    // types (carrying const string SectionName) plus AppSettings (a REST contract DTO that
    // shares the suffix). The count guard keeps the placement assertion honest -- a detector
    // that matches zero would let placement pass vacuously.
    private const int _expectedSettingsTypeCount = 7;

    // Mutation-proof reading 1: guard the detector. If the *Settings-suffix detector silently
    // matches zero, this is the test that goes RED rather than placement passing vacuously.
    [Fact]
    public void Settings_type_detector_finds_the_expected_count()
    {
        var settingsTypes = SettingsTypes();

        settingsTypes.ShouldNotBeEmpty
        (
            "§5 detector matched zero *Settings types -- the detector is broken. The placement "
                + "assertion would pass vacuously."
        );

        settingsTypes.Length.ShouldBe
        (
            _expectedSettingsTypeCount,
            "§5 expected "
                + _expectedSettingsTypeCount
                + " *Settings types, found "
                + settingsTypes.Length
                + ": "
                + string.Join(", ", settingsTypes.Select(type => type.Name).Order(StringComparer.Ordinal))
        );
    }

    // The invariant: every *Settings type lives in a subsystem folder (or Platform/), never
    // stranded at the bare Api root and never pooled in a Configuration/ or Settings/ drawer.
    [Fact]
    public void No_settings_type_lives_outside_a_subsystem_folder()
    {
        var offenders = SettingsTypes()
            .Select(type => new
            {
                type.Name,
                RootSegment = SubsystemRootSegment(type)
            })
            .Where(entry => entry.RootSegment is null
                || _forbiddenPoolSegments.Contains(entry.RootSegment, StringComparer.Ordinal))
            .Select(entry => entry.Name + " (root segment: " + (entry.RootSegment ?? "<bare Api root>") + ")")
            .Order(StringComparer.Ordinal)
            .ToArray();

        offenders.ShouldBeEmpty
        (
            "§5 requires every *Settings type to live in a subsystem folder (or Platform/), never "
                + "pooled in a Configuration/ or Settings/ drawer and never stranded at the bare Api "
                + "root. These *Settings types are mis-placed: "
                + string.Join(", ", offenders)
        );
    }

    // Detect concrete *Settings-suffixed types, scoped to Collabhost.Api.* so framework or
    // source-generated *Settings types never reach the assertion. Both class and record kinds
    // are in scope (AppSettings is a record); enums are excluded (no *Settings enum exists, and
    // a settings enum would not be a configuration type).
    private static Type[] SettingsTypes() =>
    [
        .. ArchitectureTestHelpers.AllApiTypes()
            .Where(type =>
                type is { IsClass: true, IsAbstract: false }
                && type.Name.EndsWith("Settings", StringComparison.Ordinal)
                && ArchitectureTestHelpers.RelativeToApiRoot(type.Namespace) is not null)
    ];

    // The subsystem-root segment of a type's namespace (the first segment below the Api root),
    // or null when the type sits at the bare Api root (no subsystem segment at all). For
    // "Collabhost.Api.Data.AppTypes" this returns "Data"; for a hypothetical
    // "Collabhost.Api.SomeRootSettings" with namespace "Collabhost.Api" it returns null.
    private static string? SubsystemRootSegment(Type type)
    {
        var relative = ArchitectureTestHelpers.RelativeToApiRoot(type.Namespace);

        return string.IsNullOrEmpty(relative)
            ? null
            : relative.Split('.')[0];
    }
}
