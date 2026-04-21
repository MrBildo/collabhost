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
}
