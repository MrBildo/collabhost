using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Architecture;

// Section 6 (F2) of the code-structure-conventions spec. An EF Core
// IEntityTypeConfiguration<T> lives in the subsystem folder with its entity, never pooled
// in Data/. After the move, Data/ holds no entity configurations -- only the data-access
// subsystem's own infrastructure (AppDbContext, MigrationRunner, Data/_Registration.cs, the
// Migrations/ generated-code drawer, and the blessed nested AppTypes/ subsystem). The
// invariant asserts exactly the narrow rule -- no IEntityTypeConfiguration<T> declared in
// the Collabhost.Api.Data namespace -- not an exhaustive Data/ file list.
//
// The detector keys on the type graph: a concrete type that implements the open generic
// IEntityTypeConfiguration<> is an EF entity configuration regardless of its T. Scoped to
// Collabhost.Api.* via RelativeToApiRoot so any framework-provided configuration types in
// the assembly's closure are never reached.
//
// MUTATION-PROOFING. A placement test can pass vacuously if the detector matches zero
// configurations -- then "none live in Data/" is trivially true while the move never
// happened. The count guard below asserts the detector finds the expected EF-config set
// (4 today: App, User, CapabilityOverride, ActivityEvent); if it silently matches zero the
// count guard goes RED rather than the placement test passing on an empty set. The placement
// assertion was also seen RED during authoring -- before the move all 4 configs sat in
// Collabhost.Api.Data and the placement fact named all 4 as offenders.
public class EntityConfigurationPlacementTests
{
    // The count of EF IEntityTypeConfiguration<T> implementations in the Api assembly:
    // AppConfiguration, UserConfiguration, CapabilityOverrideConfiguration,
    // ActivityEventConfiguration. The count guard keeps the placement assertion honest --
    // a detector that matches zero would let the placement fact pass vacuously.
    private const int _expectedEntityConfigurationCount = 4;

    // Mutation-proof reading 1: guard the detector. If the IEntityTypeConfiguration<>
    // detector silently matches zero, this is the test that goes RED rather than placement
    // passing vacuously.
    [Fact]
    public void Entity_configuration_detector_finds_the_expected_count()
    {
        var configurations = EntityConfigurationTypes();

        configurations.ShouldNotBeEmpty
        (
            "§6 detector matched zero IEntityTypeConfiguration<T> implementations -- the "
                + "detector is broken. The placement assertion would pass vacuously."
        );

        configurations.Length.ShouldBe
        (
            _expectedEntityConfigurationCount,
            "§6 expected "
                + _expectedEntityConfigurationCount
                + " EF entity configurations, found "
                + configurations.Length
                + ": "
                + string.Join(", ", configurations.Select(t => t.Name))
        );
    }

    // The invariant: no IEntityTypeConfiguration<T> lives in the Data/ subsystem -- each
    // resides in its entity's subsystem folder.
    [Fact]
    public void No_entity_configuration_lives_in_the_data_subsystem()
    {
        var offenders = EntityConfigurationTypes()
            .Where(type => type.Namespace == "Collabhost.Api.Data")
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        offenders.ShouldBeEmpty
        (
            "§6 requires every IEntityTypeConfiguration<T> to live in its entity's subsystem "
                + "folder, never pooled in Data/. These EF configurations are still in "
                + "Collabhost.Api.Data: "
                + string.Join(", ", offenders)
        );
    }

    // Detect concrete types implementing the open generic IEntityTypeConfiguration<>, scoped
    // to Collabhost.Api.* so framework configuration types never reach the assertion.
    private static Type[] EntityConfigurationTypes() =>
    [
        .. ArchitectureTestHelpers.AllApiTypes()
            .Where(type =>
                type is { IsClass: true, IsAbstract: false }
                && IsEntityConfiguration(type)
                && ArchitectureTestHelpers.RelativeToApiRoot(type.Namespace) is not null)
    ];

    private static bool IsEntityConfiguration(Type type) =>
        type.GetInterfaces()
            .Any(@interface =>
                @interface.IsGenericType
                && @interface.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>));
}
