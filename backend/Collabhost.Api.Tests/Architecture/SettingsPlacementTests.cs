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
// types or 7 suffix-matches.
//
// THE BARE Collabhost.Api ROOT IS IN SCOPE, by §5's own location wording. §5 forbids a
// *Settings type "outside a subsystem folder (or Platform/)" -- and the bare Collabhost.Api
// root is neither a subsystem folder nor Platform/, so a *Settings type stranded there is a §5
// violation, not an out-of-scope case. The detector therefore admits the bare root (IsUnderApiRoot)
// rather than excluding it. This is deliberately NOT done via the shared RelativeToApiRoot helper:
// that helper returns null for the bare root (its "Collabhost.Api." prefix carries a trailing dot),
// and §7's ContractRecordPlacementTests shares it -- changing the helper to admit the bare root
// would silently pull a bare-root contract record into §7's scope, which §7 makes no claim about.
// The bare-root admission is contained to this §5 call site (IsUnderApiRoot) so the blast radius
// stays here; foreign and source-generated namespaces (Microsoft.*, System.*) are still excluded.
//
// PLACEMENT RULE, machine-statable. A *Settings type satisfies placement when its namespace,
// relative to the Api root, has a non-empty subsystem-root segment (it lives inside SOME
// subsystem folder, never stranded at the bare Collabhost.Api root) and that root segment is
// not a pool-by-kind drawer name (Configuration, Settings). The forbidden-pool guard bites the
// gather-knobs-into-a-Collabhost.Api.Configuration/-drawer failure mode; the bare-root guard
// bites the strand-a-knob-at-the-root failure mode. Both are §5 violations; both are asserted.
//
// MUTATION-PROOFING. A placement test passes vacuously if the detector matches zero *Settings
// types -- then "none is mis-placed" is trivially true. The count guard below asserts the
// detector finds the full *Settings set (7 today: the 6 config types + AppSettings); a
// zero-match detector reds there rather than letting placement pass on an empty set. The
// placement decision is also exercised directly against synthetic namespaces (a bare-root
// namespace, a Configuration/ drawer namespace, a real subsystem namespace) so BOTH offender
// arms -- bare-root and pool-by-kind -- are proven to bite and name the offender, rather than
// resting on a mis-placed type that does not exist in the tree today.
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
            .Where(type => !SatisfiesPlacement(type.Namespace))
            .Select(type => type.Name + " (root segment: " + DescribeRootSegment(type.Namespace) + ")")
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

    // Mutation-proof reading 2: prove BOTH offender arms bite and name the offender, exercised
    // against synthetic namespaces so neither arm rests on a mis-placed type that does not exist
    // in the tree today. A bare-root *Settings type (namespace "Collabhost.Api") and a
    // pool-by-kind-drawer *Settings type (namespace "Collabhost.Api.Configuration") must each
    // fail placement; a real subsystem namespace must pass.
    [Fact]
    public void Placement_rule_rejects_bare_root_and_pool_by_kind_drawers()
    {
        // Bare-root: the failure mode this fix makes the test catch. Stranded directly at
        // Collabhost.Api -- no subsystem segment at all.
        SatisfiesPlacement("Collabhost.Api").ShouldBeFalse
        (
            "a *Settings type stranded at the bare Collabhost.Api root must fail §5 placement."
        );
        DescribeRootSegment("Collabhost.Api").ShouldBe("<bare Api root>");

        // Pool-by-kind: the gather-knobs-into-a-drawer failure mode.
        SatisfiesPlacement("Collabhost.Api.Configuration").ShouldBeFalse
        (
            "a *Settings type pooled in a Configuration/ drawer must fail §5 placement."
        );
        SatisfiesPlacement("Collabhost.Api.Settings").ShouldBeFalse
        (
            "a *Settings type pooled in a Settings/ drawer must fail §5 placement."
        );

        // A real subsystem folder satisfies placement; Platform/ (host-owned knobs) does too.
        SatisfiesPlacement("Collabhost.Api.Proxy").ShouldBeTrue
        (
            "a *Settings type in a subsystem folder must satisfy §5 placement."
        );
        SatisfiesPlacement("Collabhost.Api.Platform").ShouldBeTrue
        (
            "a *Settings type in Platform/ (host-owned knobs) must satisfy §5 placement."
        );
        // A nested subsystem keys on its root segment (Data.AppTypes -> Data), which is neither
        // bare-root nor a forbidden pool name.
        SatisfiesPlacement("Collabhost.Api.Data.AppTypes").ShouldBeTrue
        (
            "a *Settings type in a blessed nested subsystem must satisfy §5 placement."
        );
    }

    // Detect concrete *Settings-suffixed types under the Api root, INCLUDING the bare root, so a
    // *Settings type stranded at Collabhost.Api is not silently excluded before placement runs.
    // Foreign and source-generated *Settings types (Microsoft.*, System.*) are excluded by
    // IsUnderApiRoot. Both class and record kinds are in scope (AppSettings is a record); enums
    // are excluded (no *Settings enum exists, and a settings enum would not be a configuration
    // type).
    private static Type[] SettingsTypes() =>
    [
        .. ArchitectureTestHelpers.AllApiTypes()
            .Where(type =>
                type is { IsClass: true, IsAbstract: false }
                && type.Name.EndsWith("Settings", StringComparison.Ordinal)
                && IsUnderApiRoot(type.Namespace))
    ];

    // A *Settings type's namespace satisfies §5 placement when it has a non-empty subsystem-root
    // segment (it lives inside SOME subsystem folder, never stranded at the bare Api root) and
    // that root segment is not a forbidden pool-by-kind drawer name.
    private static bool SatisfiesPlacement(string? @namespace)
    {
        var rootSegment = SubsystemRootSegment(@namespace);

        return rootSegment is not null
            && !_forbiddenPoolSegments.Contains(rootSegment, StringComparer.Ordinal);
    }

    // The subsystem-root segment of a namespace (the first segment below the Api root), or null
    // when the namespace IS the bare Api root (no subsystem segment at all). For
    // "Collabhost.Api.Data.AppTypes" this returns "Data"; for the bare "Collabhost.Api" it
    // returns null.
    private static string? SubsystemRootSegment(string? @namespace)
    {
        if (!IsUnderApiRoot(@namespace))
        {
            return null;
        }

        var relative = ArchitectureTestHelpers.RelativeToApiRoot(@namespace);

        // RelativeToApiRoot returns null for the bare Api root (its prefix carries a trailing
        // dot the bare root does not match); that is exactly the bare-root case -> no segment.
        return string.IsNullOrEmpty(relative)
            ? null
            : relative.Split('.')[0];
    }

    // Human-readable root segment for the offender message: the segment name, or the bare-root
    // marker when a type is stranded directly at Collabhost.Api.
    private static string DescribeRootSegment(string? @namespace) =>
        SubsystemRootSegment(@namespace) ?? "<bare Api root>";

    // True when a namespace is under the Api root, INCLUDING the bare root itself. The shared
    // RelativeToApiRoot helper excludes the bare root (its "Collabhost.Api." prefix carries a
    // trailing dot); §5 must include it because a bare-root *Settings type is a §5 violation.
    // This admission is local to §5 so it does not change the shared helper that §7 relies on.
    private static bool IsUnderApiRoot(string? @namespace) =>
        @namespace is "Collabhost.Api"
            || (@namespace is not null && @namespace.StartsWith("Collabhost.Api.", StringComparison.Ordinal));
}
