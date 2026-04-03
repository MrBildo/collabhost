using Collabhost.Api.Proxy;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Proxy;

public class ProxyAppSeederTests
{
    [Fact]
    public void ResolveBinaryPath_WhitespaceOnly_Throws() =>
        Should.Throw<ArgumentException>(() => ProxyAppSeeder.ResolveBinaryPath("  "));

    [Fact]
    public void ResolveBinaryPath_NullInput_Throws() =>
        Should.Throw<ArgumentException>(() => ProxyAppSeeder.ResolveBinaryPath(null!));

    [Fact]
    public void ResolveBinaryPath_AbsolutePathThatDoesNotExist_ReturnsNull()
    {
        var result = ProxyAppSeeder.ResolveBinaryPath(@"C:\nonexistent\path\caddy.exe");

        result.ShouldBeNull();
    }

    [Fact]
    public void ResolveBinaryPath_BareName_ResolvesFromPath()
    {
        // 'where' exists on all Windows PATH
        var result = ProxyAppSeeder.ResolveBinaryPath("where");

        if (OperatingSystem.IsWindows())
        {
            result.ShouldNotBeNull();
            result.ShouldEndWith("where.exe", Case.Insensitive);
        }
    }

    [Fact]
    public void ResolveBinaryPath_BareNameNotOnPath_ReturnsNull()
    {
        var result = ProxyAppSeeder.ResolveBinaryPath("nonexistent-binary-12345");

        result.ShouldBeNull();
    }
}
