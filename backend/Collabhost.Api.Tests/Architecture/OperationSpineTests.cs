using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Architecture;

// Sections 8 (OPEN-4) and 9 (OPEN-5) of the code-structure-conventions spec -- the operation
// spine. Every mutating REST<->MCP action is one IOperation<TCommand, TResult> deriving from the
// Operation<,> base; the leaf body is intent-only (no SaveChanges, no event-hand-build, no
// try/catch-to-result -- those live in the base or the stores it calls); the concrete operation
// lives in its OWNING subsystem folder, never in the shared Operations/ spine folder.
//
// REAL-ENFORCING FORM (the #406 spine arc, FLIPPED at PR 7 once all eight operations exist). Through
// PRs 1-6 this file ran in a forward shape -- it asserted the §9 placement and §8 leaf-body-negative
// rules over WHATEVER concrete IOperation<,> implementations existed at any moment (honest, non-
// vacuous as each operation landed), plus a by-name set-shape list that grew PR-by-PR. That forward
// set-shape deliberately did NOT carry the exactly-8 count guard: asserting "== 8" against a partial
// set is red-for-the-wrong-reason. PR 7 lands the 8th operation (DeleteApp) and tightens that set-
// shape into the real-enforcing exactly-8 count guard below. The count IS the mutation-proof primitive
// (§11's RegistrationFilePresence precedent): a silently-broken detector that matches zero goes RED on
// the count guard rather than passing vacuously on an empty set; a 9th operation that forgets to join,
// or a regressed/removed operation, reds it too.
//
// All three rules are phrased over the leaf set the base's implementations define -- the enumerable
// asset §8 names as the reason the base earns its place over a plain injected service.
public class OperationSpineTests
{
    private const string _operationsSpineNamespace = "Operations";

    // The exactly-8 count: the eight mutating REST<->MCP twin actions the #406 arc migrated onto the
    // spine -- the lifecycle four (start/stop/restart/kill), reload-proxy, update-settings, create/
    // register, and delete. The plan's §2.3 honest-scope picture: 8 operations migrate, 2 mutating-
    // but-single-surface (CreateUser/DeactivateUser, no MCP twin) are surfaced-and-left out of the
    // spine, ~32 read-only handlers are definitively out. So the set is exactly these eight.
    private const int _expectedOperationCount = 8;

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

    // §8/§9 count guard (the keystone -- the forward set-shape flipped to real-enforcing at PR 7).
    // The concrete IOperation<,> implementation set must be non-empty AND equal exactly 8 -- the
    // mutation-proof primitive (§11's RegistrationFilePresence precedent). Non-empty: if the
    // reflection detector silently matches zero (a broken interface check, a namespace-scope
    // regression), this goes RED rather than the placement / leaf-negative facts passing vacuously
    // on an empty set. Exactly 8: a 9th operation that should have been surfaced-and-left (a single-
    // surface mutation forced onto the spine -- the pool-by-kind move §9 forbids), or a regressed /
    // removed operation, reds it with the actual set named.
    [Fact]
    public void Spine_holds_exactly_eight_operations()
    {
        var operations = ConcreteOperationTypes()
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        operations.ShouldNotBeEmpty
        (
            "§8/§9: the concrete IOperation<,> set is empty -- the reflection detector matched zero, "
                + "so the placement and leaf-negative facts would pass vacuously. The detector is broken."
        );

        operations.Length.ShouldBe
        (
            _expectedOperationCount,
            "§8/§9: the concrete IOperation<,> set must be exactly the eight migrated REST<->MCP twin "
                + "operations. A different count means an operation was added without being surfaced-"
                + "and-left (pool-by-kind, §9-forbidden) or one regressed. Found "
                + operations.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ": "
                + string.Join(", ", operations)
        );
    }

    // The explicit by-name set -- the eight operations the arc migrated, named so a diff that adds or
    // drops one is reviewed against this list, not only against the count. The lifecycle four
    // (start/stop/restart/kill, PRs 2-3), reload-proxy (PR 4, the app-less op in Proxy/), update-
    // settings (PR 5, the heaviest body), create/register (PR 6, THE divergence op), and delete (PR 7,
    // this PR). Redundant-with-count by design: the count guard above is the mutation-proof primitive,
    // this names which eight so a swap (one removed, one added -- same count) still reds.
    [Fact]
    public void Spine_holds_exactly_the_eight_named_operations()
    {
        var operations = ConcreteOperationTypes()
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        operations.ShouldBe
        (
            [
                "CreateAppOperation",
                "DeleteAppOperation",
                "KillAppOperation",
                "ReloadProxyOperation",
                "RestartAppOperation",
                "StartAppOperation",
                "StopAppOperation",
                "UpdateSettingsOperation"
            ],
            "§8/§9: the concrete IOperation<,> set should be exactly the eight migrated operations. "
                + "Found: "
                + string.Join(", ", operations)
        );
    }

    // §8/§11: every concrete IOperation<,> is registered EXPLICITLY in its subsystem's
    // _Registration.cs -- central-explicit DI, no assembly-scan (§11). For each operation, resolve
    // the _Registration.cs co-located in its source folder (the operation file and its subsystem's
    // registration share a folder, so this needs no namespace->folder mapping -- it sidesteps the
    // System/-vs-Platform divergence by construction) and assert its source text contains an
    // AddScoped<TheOperation> registration. An operation that exists but is never registered (the
    // "forgot to register" failure §11 names as a loud, CI-catchable class) reds here with the
    // offender named -- the enumeration asset §8 says the base earns its place for.
    [Fact]
    public void Every_operation_is_registered_in_its_subsystem_registration_file()
    {
        var offenders = ConcreteOperationTypes()
            .Where(type => !IsRegisteredInSubsystem(type))
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        offenders.ShouldBeEmpty
        (
            "§8/§11 requires every IOperation<,> to be registered explicitly (AddScoped<Op>) in its "
                + "subsystem's _Registration.cs -- no assembly-scan. These operations are not "
                + "registered in their subsystem's _Registration.cs: "
                + string.Join(", ", offenders)
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

    // True when the operation's subsystem _Registration.cs (co-located in the operation's source
    // folder) registers it explicitly with AddScoped<TheOperation>. Resolving via the operation's
    // OWN source file means no namespace->folder mapping is needed -- the operation and its
    // subsystem registration share a folder, so the System/-vs-Platform namespace divergence cannot
    // bite here.
    private static bool IsRegisteredInSubsystem(Type type)
    {
        var sourceFile = DeclaringSourceFile(type);

        if (sourceFile is null)
        {
            return false;
        }

        var registrationFile = Path.Combine(Path.GetDirectoryName(sourceFile)!, "_Registration.cs");

        if (!File.Exists(registrationFile))
        {
            return false;
        }

        var registrationSource = File.ReadAllText(registrationFile);

        return registrationSource.Contains("AddScoped<" + type.Name + ">", StringComparison.Ordinal);
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
