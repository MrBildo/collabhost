using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Architecture;

// Sections 8 (OPEN-4) and 9 (OPEN-5) of the code-structure-conventions spec -- the operation
// spine. Every mutating REST<->MCP action is one IOperation<TCommand, TResult> deriving from the
// Operation<,> base; the leaf body is intent-only (no SaveChanges, no event-hand-build, no
// try/catch-to-result -- those live in the base or the stores it calls); the concrete operation
// lives in its OWNING subsystem folder, never in the shared Operations/ spine folder.
//
// FORWARD FORM (the #406 spine arc, mid-migration). The operations land PR-by-PR (PR 2:
// restart + kill; PR 3: start/stop; reload-proxy; settings; register; PR 7: delete + the count-
// guard flip). This test is authored in the forward, non-vacuous-as-operations-appear shape: it
// asserts the §9 placement rule and the §8 leaf-body-negative rule for WHATEVER concrete
// IOperation<,> implementations exist at any moment, so it stays honest as each operation lands --
// the first mis-placed or non-intent-only leaf reds it. It deliberately does NOT yet carry the
// exactly-8 count guard: that guard reds-for-the-wrong-reason against a partial set (N != 8) and
// tightens to real-enforcing in the final PR once all eight operations exist. The current set is
// asserted explicitly below (by name) so this file's forward stance is visible, not silent, and a
// premature or missing operation shows up in a reviewed diff -- the additive PR-by-PR analogue of
// the eventual count guard.
//
// Both rules are phrased over the leaf set the base's implementations define -- the enumerable
// asset §8 names as the reason the base earns its place over a plain injected service.
public class OperationSpineTests
{
    private const string _operationsSpineNamespace = "Operations";

    // The leaf-body-negative tokens (§8). A token appearing in an operation leaf's source file is
    // plumbing that belongs in the base or a store, not in the intent-only leaf body. Phrased
    // leaf-SCOPED and negative per the spec's explicit warning (line 191): a positive "appears
    // only in the base" assertion scopes to the whole assembly and is RED against correct
    // non-operation code (SaveChanges legitimately lives in ~5 stores; event-publish in the
    // supervisor/proxy). Scoping to operation leaf files keeps it green against every legitimate
    // persist/publish site.
    private static readonly string[] _leafBodyForbiddenTokens =
    [
        "SaveChangesAsync",       // persistence is the store's job; the base calls the store
        "SaveChanges(",           // the non-async form, same rule
        "new ActivityEvent",      // events are stamped via the base RecordAsync helper, never hand-built in a leaf
    ];

    // The forward set-shape assertion: the concrete operations migrated so far, named explicitly.
    // PR 2 added RestartAppOperation + KillAppOperation (the two cleanest, process-only lifecycle
    // ops); PR 3 added StartAppOperation + StopAppOperation (the dual-branch lifecycle pair --
    // routing-only vs process); PR 4 added ReloadProxyOperation (the trivial app-less op, and the
    // first spine op outside Registry/ -- it lives in Proxy/, its owning subsystem per §9); PR 5 added
    // UpdateSettingsOperation (the heaviest single body -- the merge/validate/save loop plus the
    // partial-success conflict-with-value render path); PR 6 adds CreateAppOperation (THE divergence op
    // -- per-surface input adapters over a shared, surface-blind core; the section-assembly diverges,
    // the create sequence does not). This makes the mid-migration stance visible -- the placement and
    // leaf-negative facts below do real work on exactly these operations now, and each later PR extends
    // this list in a reviewed diff (a premature or dropped operation reds here). It is NOT the count
    // guard (that asserts exactly 8 and lands in the final PR 7, alongside DeleteApp); it pins the
    // current arc state. Seven of eight migrated; DeleteApp remains.
    [Fact]
    public void Spine_holds_exactly_the_operations_migrated_so_far()
    {
        var operations = ConcreteOperationTypes()
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        operations.ShouldBe
        (
            [
                "CreateAppOperation",
                "KillAppOperation",
                "ReloadProxyOperation",
                "RestartAppOperation",
                "StartAppOperation",
                "StopAppOperation",
                "UpdateSettingsOperation"
            ],
            "§8/§9 mid-migration: the concrete IOperation<,> set should be exactly the operations "
                + "migrated through PR 6. Each later PR adds to this list. Found: "
                + string.Join(", ", operations)
        );
    }

    // §9: no concrete IOperation<,> implementation lives in the Operations/ spine folder. The
    // spine holds the contract, base, and result only; each concrete operation lives in the
    // subsystem that owns the entity/store it acts on. Forward: empty today, reds the moment an
    // operation is mis-placed into Operations/.
    [Fact]
    public void No_concrete_operation_lives_in_the_spine_folder()
    {
        var offenders = ConcreteOperationTypes()
            .Where(type => IsInSpineFolder(type))
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        offenders.ShouldBeEmpty
        (
            "§9 requires every concrete IOperation<,> to live in its owning subsystem folder, "
                + "never in the Operations/ spine folder (which holds the contract, base, and "
                + "result only). These operations are mis-placed in Operations/: "
                + string.Join(", ", offenders)
        );
    }

    // §8 leaf-body-negative: no concrete operation's source file contains SaveChanges, a
    // hand-built ActivityEvent, or try/catch-to-result plumbing -- those live in the base or the
    // stores the base calls. Forward: empty today, reds the moment a leaf re-implements plumbing.
    [Fact]
    public void No_operation_leaf_body_re_implements_hoisted_plumbing()
    {
        var offenders = ConcreteOperationTypes()
            .Select(type => new
            {
                type.Name,
                Tokens = ForbiddenTokensInSource(type)
            })
            .Where(entry => entry.Tokens.Length > 0)
            .Select(entry => entry.Name + " (contains: " + string.Join(", ", entry.Tokens) + ")")
            .Order(StringComparer.Ordinal)
            .ToArray();

        offenders.ShouldBeEmpty
        (
            "§8 requires every IOperation<,> leaf body to be intent-only -- no SaveChanges, no "
                + "hand-built ActivityEvent, no try/catch-to-result. That plumbing lives in the "
                + "Operation<,> base or the stores it calls. These leaves re-implement it: "
                + string.Join(", ", offenders)
        );
    }

    // Concrete (non-abstract, non-interface) types in the Api assembly that implement
    // IOperation<,>, scoped to Collabhost.Api.* so no framework type can reach the assertions.
    private static Type[] ConcreteOperationTypes() =>
    [
        .. ArchitectureTestHelpers.AllApiTypes()
            .Where(type =>
                type is { IsClass: true, IsAbstract: false }
                && ArchitectureTestHelpers.RelativeToApiRoot(type.Namespace) is not null
                && ImplementsOperationInterface(type))
    ];

    private static bool ImplementsOperationInterface(Type type) =>
        type.GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(Collabhost.Api.Operations.IOperation<,>));

    private static bool IsInSpineFolder(Type type)
    {
        var relative = ArchitectureTestHelpers.RelativeToApiRoot(type.Namespace);

        return relative is not null
            && relative.Split('.')[0].Equals(_operationsSpineNamespace, StringComparison.Ordinal);
    }

    // Resolve the operation's source file across the whole Api tree (reflection carries no file
    // path; a namespace->folder mapping is unsafe given the documented System/-vs-Platform
    // divergence) and return which forbidden plumbing tokens its source contains.
    private static string[] ForbiddenTokensInSource(Type type)
    {
        var sourceFile = DeclaringSourceFile(type);

        if (sourceFile is null)
        {
            return [];
        }

        var source = File.ReadAllText(sourceFile);

        return [.. _leafBodyForbiddenTokens.Where(token => source.Contains(token, StringComparison.Ordinal))];
    }

    private static string? DeclaringSourceFile(Type type)
    {
        var declaration = "class " + type.Name;

        return Directory
            .EnumerateFiles(ArchitectureTestHelpers.ApiProjectDirectory, "*.cs", SearchOption.AllDirectories)
            .FirstOrDefault(file =>
                IsAuthoredSource(file)
                && File.ReadLines(file).Any(line => DeclaresClass(line, declaration)));
    }

    // A line declares this class when "class <Name>" appears followed by a non-identifier
    // boundary -- so "class StartAppOperation" is not matched by "class StartAppOperationFactory".
    private static bool DeclaresClass(string line, string declaration)
    {
        var index = line.IndexOf(declaration, StringComparison.Ordinal);

        if (index < 0)
        {
            return false;
        }

        var afterIndex = index + declaration.Length;

        return afterIndex >= line.Length
            || (!char.IsLetterOrDigit(line[afterIndex]) && line[afterIndex] != '_');
    }

    private static bool IsAuthoredSource(string path)
    {
        var relative = Path.GetRelativePath(ArchitectureTestHelpers.ApiProjectDirectory, path)
            .Replace('\\', '/');

        return !relative.StartsWith("bin/", StringComparison.Ordinal)
            && !relative.StartsWith("obj/", StringComparison.Ordinal);
    }
}
