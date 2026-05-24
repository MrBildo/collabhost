using Collabhost.Api.Platform;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Platform;

public class VersionInfoTests
{
    [Fact]
    public void StripCommitHash_WithoutSuffix_ReturnsRawValue()
    {
        var result = VersionInfo.StripCommitHash("1.2.3");

        result.ShouldBe("1.2.3");
    }

    [Fact]
    public void StripCommitHash_WithCommitHashSuffix_StripsSuffix()
    {
        var result = VersionInfo.StripCommitHash("0.1.0+abc1234");

        result.ShouldBe("0.1.0");
    }

    [Fact]
    public void StripCommitHash_WithEmptyString_ReturnsEmptyString()
    {
        var result = VersionInfo.StripCommitHash("");

        result.ShouldBe("");
    }

    [Fact]
    public void StripCommitHash_WithPlusThenEmptyHash_ReturnsVersionOnly()
    {
        var result = VersionInfo.StripCommitHash("2.0.0+");

        result.ShouldBe("2.0.0");
    }

    [Fact]
    public void Current_IsNonEmpty() =>
        VersionInfo.Current.ShouldNotBeNullOrWhiteSpace();

    [Fact]
    public void Current_DoesNotContainPlusSuffix() =>
        VersionInfo.Current.ShouldNotContain("+");

    [Fact]
    public void Platform_IsNonEmpty() =>
        VersionInfo.Platform.ShouldNotBeNullOrWhiteSpace();

    // The Lazy-resolved property must never throw or return null. In a test build
    // (no WwwrootHash MSBuild property) the AssemblyMetadataAttribute is not emitted,
    // so the returned value is empty string. In an archive-published build it is the
    // 64-hex digest. Either way, a non-null string. Card #342.
    [Fact]
    public void WwwrootHash_IsNonNullString() =>
        VersionInfo.WwwrootHash.ShouldNotBeNull();

    [Fact]
    public void WwwrootHash_WhenEmpty_DoesNotContainWhitespace()
    {
        // Defensive: if the attribute reads as empty (dev build), the value is the empty
        // string, not whitespace. PortalIntegrityCheck.Validate treats whitespace and empty
        // identically (both -> Unknown), but the contract here is "empty exactly."
        var value = VersionInfo.WwwrootHash;

        if (value.Length == 0)
        {
            value.ShouldBe(string.Empty);
        }
        else
        {
            // Non-empty values must look like a SHA-256 hex digest -- 64 lowercase hex.
            value.Length.ShouldBe(64);
            value.ShouldMatch("^[0-9a-f]{64}$");
        }
    }
}
