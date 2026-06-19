using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Architecture;

// Section 7 (OPEN-3) of the code-structure-conventions spec. A subsystem's request /
// response / command / arguments shapes live together in a *Contracts.cs grouping file in
// that subsystem; surface files (endpoints, MCP tools) hold only their file-scoped mapping
// to and from those shapes, never the shapes themselves. The invariant asserted here is
// PLACEMENT, not count: a subsystem may carry more than one *Contracts.cs (Registry has
// _ApiContracts + _AppTypeContracts; Probes has _ApiContracts + _EvidenceContracts), so the
// test must never assert one-file-per-folder.
//
// Scoped to PUBLIC records, exactly as §7 reads ("no PUBLIC *Request/... record outside a
// *Contracts.cs file"). The public qualifier is load-bearing: Authorization/UserEndpoints.cs
// declares internal CreateUserRequest / MeResponse records that are deliberately file-private
// contract DTOs, NOT in scope -- including them would falsely flag two records and contradict
// the ratified "exactly 1 stranded record" accounting. Reflection's IsPublic is the
// authoritative qualifier check (a regex over access modifiers is fragile).
//
// The detector is type-graph for the public + suffix qualifier, then resolves each match's
// declaring source file (reflection carries no file path) to assert the file name belongs to
// the *Contracts.cs suffix family. Scoped to Collabhost.Api.* via RelativeToApiRoot so no
// framework or source-generated type reaches the assertion.
//
// MUTATION-PROOFING. A placement test passes vacuously if the detector matches zero contract
// records. The count guard below asserts the detector finds the full public-contract set
// (13 today); a zero-match detector reds there rather than letting placement pass on an empty
// set. The placement assertion was also seen RED during authoring -- before the extract,
// ActivityEventListResponse was declared inline in ActivityLogEndpoints.cs and named as the
// offender.
public class ContractRecordPlacementTests
{
    private static readonly string[] _contractSuffixes =
    [
        "Request",
        "Response",
        "Command",
        "Arguments"
    ];

    // The count of public *Request/*Response/*Command/*Arguments types in the Api assembly.
    // 21 today: 13 from the T4 P2 baseline, plus the two *Command records the #406 spine PR 2 adds
    // (RestartAppCommand + KillAppCommand), the two PR 3 adds (StartAppCommand + StopAppCommand), all
    // in Registry/_OperationContracts.cs; plus the one PR 4 adds (ReloadProxyCommand, in
    // Proxy/_OperationContracts.cs -- the first spine command outside Registry/); plus the one PR 5
    // adds (UpdateSettingsCommand, in Registry/_OperationContracts.cs); plus the one PR 6 adds
    // (CreateAppCommand, in Registry/_OperationContracts.cs); plus the one PR 7 adds (DeleteAppCommand,
    // in Registry/_OperationContracts.cs). The pre-existing CreateAppRequest + CreateAppResponse were
    // already in the baseline count and are unaffected by the migration. PR 5's UpdateSettingsOutcome,
    // PR 6's CreateAppOutcome, and PR 7's DeleteAppOutcome are *Outcome records, NOT in the *Command/
    // *Request/*Response/*Arguments suffix family, so they are not counted (same as AppActionOutcome /
    // ProxyReloadOutcome). Each spine PR that adds a public *Command record bumps this count. The count
    // guard keeps the placement assertion honest -- a detector that matches zero would let placement
    // pass vacuously.
    private const int _expectedContractRecordCount = 21;

    // Mutation-proof reading 1: guard the detector. If the public-contract detector silently
    // matches zero, this is the test that goes RED rather than placement passing vacuously.
    [Fact]
    public void Contract_record_detector_finds_the_expected_count()
    {
        var contracts = PublicContractTypes();

        contracts.ShouldNotBeEmpty
        (
            "§7 detector matched zero public contract records -- the detector is broken. "
                + "The placement assertion would pass vacuously."
        );

        contracts.Length.ShouldBe
        (
            _expectedContractRecordCount,
            "§7 expected "
                + _expectedContractRecordCount
                + " public contract records, found "
                + contracts.Length
                + ": "
                + string.Join(", ", contracts.Select(type => type.Name).Order(StringComparer.Ordinal))
        );
    }

    // The invariant: every public contract record is declared inside a *Contracts.cs grouping
    // file, never stranded in a surface file (endpoints, MCP tools).
    [Fact]
    public void No_public_contract_record_lives_outside_a_contracts_file()
    {
        var offenders = PublicContractTypes()
            .Select(type => new
            {
                type.Name,
                File = DeclaringFileName(type)
            })
            .Where(entry => entry.File is null || !IsContractsFile(entry.File))
            .Select(entry => entry.Name + " (in " + (entry.File ?? "unresolved") + ")")
            .Order(StringComparer.Ordinal)
            .ToArray();

        offenders.ShouldBeEmpty
        (
            "§7 requires every public *Request/*Response/*Command/*Arguments record to live in "
                + "a *Contracts.cs grouping file, never stranded in a surface file. These records "
                + "are declared outside a contracts file: "
                + string.Join(", ", offenders)
        );
    }

    private static Type[] PublicContractTypes() =>
    [
        .. ArchitectureTestHelpers.AllApiTypes()
            .Where(type =>
                type is { IsPublic: true, IsClass: true, IsNested: false }
                && _contractSuffixes.Any(suffix => type.Name.EndsWith(suffix, StringComparison.Ordinal))
                && ArchitectureTestHelpers.RelativeToApiRoot(type.Namespace) is not null)
    ];

    // Resolve the source file a type is declared in. Reflection carries no file path, so the
    // declaring file is found by locating the type's declaration across the whole Api source
    // tree. A namespace->folder mapping is NOT used: the System/ folder deliberately declares
    // namespace Collabhost.Api.Platform (the documented System/-vs-Platform divergence), so a
    // namespace-derived folder lookup misses VersionResponse, which physically lives in
    // System/_ApiContracts.cs. A whole-tree search by the type's declaration sidesteps the
    // divergence entirely. Returns the bare file name (e.g. "_ApiContracts.cs") or null if
    // unresolved.
    private static string? DeclaringFileName(Type type)
    {
        var declaration = "record " + type.Name;

        var declaringFile = Directory
            .EnumerateFiles(ArchitectureTestHelpers.ApiProjectDirectory, "*.cs", SearchOption.AllDirectories)
            .FirstOrDefault(file =>
                IsAuthoredSource(file)
                && File.ReadLines(file).Any(line => DeclaresRecord(line, declaration)));

        return declaringFile is null ? null : Path.GetFileName(declaringFile);
    }

    // A line declares this record when "record <Name>" appears followed by a non-identifier
    // boundary -- so "record VersionResponse" is not matched by "record VersionResponseV2".
    private static bool DeclaresRecord(string line, string declaration)
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

    private static bool IsContractsFile(string fileName) =>
        fileName.EndsWith("Contracts.cs", StringComparison.Ordinal);
}
