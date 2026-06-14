using System.Text.RegularExpressions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Architecture;

// Section 2a of the code-structure-conventions spec. No source file contains a region
// directive or a banner-comment divider. A file is a flat whitespace-separated list of
// members, and sectioning is blank-line breathing room rather than regions or banners.
// GREEN on the current tree because the Api project has zero region directives. This is
// the floor, enforced uniformly rather than newly adopted.
public partial class NoRegionDirectivesTests
{
    // A banner divider is a comment line that is almost entirely repeated punctuation,
    // the rule-off-a-section shape that section 2a forbids. Length is bounded for MA0009
    // and capture is explicit because the test never reads any group.
    [GeneratedRegex
    (
        @"^\s{0,400}//\s{0,8}[-=*#_]{4,400}\s{0,8}$",
        RegexOptions.ExplicitCapture
    )]
    private static partial Regex BannerDividerLine { get; }

    [Fact]
    public void No_source_file_contains_a_region_directive()
    {
        var offenders = EnumerateApiSourceFiles()
            .Where(file => File.ReadLines(file).Any(ContainsRegionDirective))
            .Select(ToRelativePath)
            .Order(StringComparer.Ordinal)
            .ToArray();

        offenders.ShouldBeEmpty
        (
            "§2a forbids #region; these source files contain one: "
                + string.Join(", ", offenders)
        );
    }

    [Fact]
    public void No_source_file_contains_a_banner_comment_divider()
    {
        var offenders = EnumerateApiSourceFiles()
            .Where(file => File.ReadLines(file).Any(line => BannerDividerLine.IsMatch(line)))
            .Select(ToRelativePath)
            .Order(StringComparer.Ordinal)
            .ToArray();

        offenders.ShouldBeEmpty
        (
            "§2a forbids banner-comment dividers (// ===== rule-offs); these files contain one: "
                + string.Join(", ", offenders)
        );
    }

    private static bool ContainsRegionDirective(string line)
    {
        var trimmed = line.TrimStart();

        return trimmed.StartsWith("#region", StringComparison.Ordinal)
            || trimmed.StartsWith("#endregion", StringComparison.Ordinal);
    }

    private static IEnumerable<string> EnumerateApiSourceFiles()
    {
        var apiDir = ArchitectureTestHelpers.ApiProjectDirectory;

        return Directory
            .EnumerateFiles(apiDir, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedOrOutput(path));
    }

    private static bool IsGeneratedOrOutput(string path)
    {
        var relative = Path.GetRelativePath(ArchitectureTestHelpers.ApiProjectDirectory, path)
            .Replace('\\', '/');

        // Build output and generated code are not authored source — §2a governs
        // what the team writes, not what the SDK / EF tooling emits.
        return relative.StartsWith("bin/", StringComparison.Ordinal)
            || relative.StartsWith("obj/", StringComparison.Ordinal)
            || relative.Contains("/Migrations/", StringComparison.Ordinal)
            || relative.EndsWith(".g.cs", StringComparison.Ordinal);
    }

    private static string ToRelativePath(string path) =>
        Path.GetRelativePath(ArchitectureTestHelpers.ApiProjectDirectory, path).Replace('\\', '/');
}
