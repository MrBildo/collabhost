using System.Reflection;
using System.Text.RegularExpressions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Platform;

// Regression guard for the class-of-bug behind card #176 CI breakage: xunit captures
// Console.Out per-test via a StringWriter. If Program.cs (or any code the
// WebApplicationFactory startup path touches) calls Console.Write* SYNCHRONOUSLY, the call
// lands on whatever per-test StringWriter xunit currently has installed. When a subsequent
// test's WebApplicationFactory re-runs Program.cs top-level statements, the prior test's
// StringWriter is already disposed; the synchronous write throws ObjectDisposedException
// and kills host startup -- cascading through the whole collection (#176 CI fallout).
//
// A runtime safeguard is impractical: the Console logger provider (enabled by default in
// ASP.NET Core hosting and explicitly registered in Program.cs's preflight logger factory)
// writes framing through Console.Out on the startup thread. A runtime test can't
// distinguish "StartupPreflight's ILogger output routed via Console provider" (safe -- the
// provider has its own exception-swallowing layer) from "direct Console.Write* during
// startup" (the actual bug class). Any runtime check therefore either produces false
// positives or fails to catch the specific pattern.
//
// Source-level check it is. Program.cs top-level statements run during WebApplicationFactory
// startup; any Console.Write* in that file (outside the --version short-circuit branch that
// never fires during tests) is the regression signal. Also checks other startup-path files.
public partial class NoSynchronousConsoleInStartupTests
{
    // Startup-path files. Any new file added to this list should have been audited for
    // Console.Write* calls before inclusion. The canonical answer to "can I add
    // Console.Write here?" is "no, use ILogger."
    private static readonly string[] _startupFilesRelative =
    [
        "Collabhost.Api/Program.cs",
        "Collabhost.Api/Authorization/UserSeedService.cs",
        "Collabhost.Api/Proxy/ProxyAppSeeder.cs",
        "Collabhost.Api/Data/MigrationRunner.cs"
    ];

    // Match Console.Write, Console.WriteLine, Console.Error.Write, Console.Error.WriteLine,
    // Console.Out.Write, Console.Out.WriteLine -- any synchronous poke at System.Console.
    // Bounded whitespace keeps the analyzer happy (MA0009) without changing semantics.
    [GeneratedRegex(@"\bConsole\.(?:Out|Error)?\.?Write(?:Line)?\s{0,8}\(", RegexOptions.None)]
    private static partial Regex ConsoleWritePattern { get; }

    // Line comment stripper. Bounded non-newline consumption (MA0009).
    [GeneratedRegex(@"//[^\n]{0,4000}", RegexOptions.Multiline)]
    private static partial Regex LineCommentPattern { get; }

    // Block comment stripper -- /* ... */ including newlines. Bounded (MA0009).
    [GeneratedRegex(@"/\*.{0,20000}?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockCommentPattern { get; }

    // Strips the version-flag short-circuit block in Program.cs from the scanned text.
    // Shape of the block is an args.Any check on "--version" with a Console.WriteLine
    // payload and a return 0. That block exits before any WebApplicationFactory sees it,
    // so a Console.WriteLine inside it is safe. Body bounded to 2000 chars (MA0009).
    // Uses non-greedy `.{0,N}?` to tolerate the inner parentheses of args.Any(...) .
    [GeneratedRegex(@"if\s{0,8}\(\s{0,8}args\.Any.{0,400}?""--version"".{0,400}?\)\s{0,8}\{.{0,2000}?\}", RegexOptions.Singleline)]
    private static partial Regex VersionBranchPattern { get; }

    [Fact]
    public void StartupCodePaths_DoNotContainSynchronousConsoleWriteCalls()
    {
        var repoRoot = FindRepoRoot();

        foreach (var relativePath in _startupFilesRelative)
        {
            var fullPath = Path.Combine(repoRoot, "backend", relativePath);

            File.Exists(fullPath).ShouldBeTrue
            (
                $"Startup file missing: {fullPath}. The file list in this test is stale."
            );

            var source = File.ReadAllText(fullPath);

            var stripped = StripCommentsAndVersionBranch(source);

            var matches = ConsoleWritePattern.Matches(stripped);

            matches.Count.ShouldBe
            (
                0,
                $"{relativePath} contains synchronous Console.Write* calls on the startup path. " +
                $"These land on xunit's per-test StringWriter capture and blow up subsequent tests once " +
                $"the writer disposes (#176 CI fallout). Use ILogger instead -- it writes through the " +
                $"Console provider's background queue, which swallows disposal exceptions. Matches: " +
                $"{string.Join(", ", matches.Select(m => m.Value))}"
            );
        }
    }

    private static string StripCommentsAndVersionBranch(string source)
    {
        var noLineComments = LineCommentPattern.Replace(source, string.Empty);
        var noBlockComments = BlockCommentPattern.Replace(noLineComments, string.Empty);
        var noVersionBranch = VersionBranchPattern.Replace(noBlockComments, string.Empty);

        return noVersionBranch;
    }

    private static string FindRepoRoot()
    {
        // Walk up from the assembly location until we find the `backend/Collabhost.Api`
        // directory. In CI and local runs the tests execute out of
        // Collabhost.Api.Tests/bin/Debug/net10.0/ which is five levels deep.
        var start = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        var current = start;

        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current, "backend", "Collabhost.Api")))
            {
                return current;
            }

            current = Path.GetDirectoryName(current);
        }

        throw new InvalidOperationException($"Could not find repo root starting from {start}.");
    }
}
