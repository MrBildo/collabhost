using Collabhost.Api.StaticSite;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.StaticSite;

// Card #377: the pure predicate must fire IFF all three conditions hold --
// overlay route active AND values registered AND file absent -- and stay silent
// on every legitimate state. This is the complete truth table over the three
// boolean inputs (2^3 = 8 rows); only the (true, true, false) row is an orphan.
public class RuntimeConfigOverlayOrphanCheckTests
{
    // The single unsafe state: route active, values registered, file absent.
    [Fact]
    public void IsOrphaned_RouteActive_ValuesRegistered_FileAbsent_IsTrue() =>
        RuntimeConfigOverlayOrphanCheck.IsOrphaned
        (
            routeActive: true,
            valuesRegistered: true,
            fileExists: false
        ).ShouldBeTrue();

    // File present -> already rendered, not an orphan.
    [Fact]
    public void IsOrphaned_FilePresent_IsFalse() =>
        RuntimeConfigOverlayOrphanCheck.IsOrphaned
        (
            routeActive: true,
            valuesRegistered: true,
            fileExists: true
        ).ShouldBeFalse();

    // No values registered -> writer no-ops by design, nothing expected on disk.
    [Fact]
    public void IsOrphaned_NoValuesRegistered_IsFalse() =>
        RuntimeConfigOverlayOrphanCheck.IsOrphaned
        (
            routeActive: true,
            valuesRegistered: false,
            fileExists: false
        ).ShouldBeFalse();

    // Route inactive (operator stopped) -> nothing is being served to 404.
    [Fact]
    public void IsOrphaned_RouteInactive_IsFalse() =>
        RuntimeConfigOverlayOrphanCheck.IsOrphaned
        (
            routeActive: false,
            valuesRegistered: true,
            fileExists: false
        ).ShouldBeFalse();

    // Remaining truth-table rows: any state that is not exactly
    // (active, registered, absent) is silent.
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    public void IsOrphaned_AllOtherStates_AreFalse
    (
        bool routeActive,
        bool valuesRegistered,
        bool fileExists
    ) =>
        RuntimeConfigOverlayOrphanCheck.IsOrphaned(routeActive, valuesRegistered, fileExists)
            .ShouldBeFalse();
}
