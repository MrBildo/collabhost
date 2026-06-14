using System.Collections.Frozen;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Architecture;

// Section 4 (OPEN-1) of the code-structure-conventions spec. Inside a subsystem,
// files cluster flat by filename prefix. A .cs sub-folder under a subsystem is legal
// ONLY when its name is an entry in this blessed-subfolders manifest. The default is
// flat; taxonomy never earns a folder. Anything else fails the build.
//
// THE MANIFEST IS THIS TEST'S OWN INPUT. The FrozenSet literal below IS the machine-read
// manifest, never a markdown list in the convention doc (the one non-negotiable). Growing
// it is a deliberate, diff-visible act gated by review; the bound-set justification lives
// in section 4's prose and is never machine-read. Adding a folder is adding one string
// literal in a reviewed diff, and the build stays red until you do.
//
// SEEDED GREEN against the current tree (S86 measurement). The five entries below are
// exactly the five real two-segment .cs namespaces below a subsystem, no more (no
// speculative pre-blessing). Two of the five are seeded for a stated reason rather than
// being feature sub-folders; the inline comments on those entries record which and why.
public class BlessedSubfoldersTests
{
    private static readonly FrozenSet<string> _blessedSubfolders = new[]
    {
        "Supervisor.Containment",
        "Supervisor.Resources",
        "Capabilities.Configurations",
        "Data.Migrations",      // EF-generated .cs, a generated-code drawer, not a feature sub-folder
        "Data.AppTypes",        // the AppType subsystem, blessed-nested under Data per section 6
    }.ToFrozenSet(StringComparer.Ordinal);

    [Fact]
    public void Every_subsystem_subfolder_is_blessed()
    {
        var offenders = ArchitectureTestHelpers.AllApiTypes()
            .Select(t => t.Namespace)
            .Select(ArchitectureTestHelpers.RelativeToApiRoot)               // strip the Api root; null for foreign / source-generated
            .Where(rel => rel is not null && rel.Contains('.', StringComparison.Ordinal))   // a segment below a subsystem
            .Distinct(StringComparer.Ordinal)
            .Where(rel => !_blessedSubfolders.Contains(rel!))
            .Order(StringComparer.Ordinal)
            .ToArray();

        offenders.ShouldBeEmpty
        (
            "Section 4: these sub-folder namespaces are not in the blessed-subfolders manifest "
                + "(a flat-prefix default was violated, or the manifest needs a reviewed entry): "
                + string.Join(", ", offenders)
        );
    }
}
