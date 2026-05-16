using Collabhost.Api.Registry;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Registry;

// Covers #326 / #322 decision E1 -- the pure per-app writable data path
// resolver that backs the registration / get_app contract field
// `writableDataPath`. Path composition only; no I/O. The path shape is the
// stable v1 contract (`<dataRoot>/app-data/<slug>`), coordinated with the
// INSTALL.md §5.6 wording (#324). Distinct from the removed Axis-B `app-cwd`.
public class AppDataPathResolverTests
{
    [Fact]
    public void ResolvePath_ComposesPerAppPathUnderDataRootAppData()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "collabhost-appdata-resolve");

        var resolved = AppDataPathResolver.ResolvePath(dataRoot, "collaboard");

        resolved.ShouldBe(Path.GetFullPath(Path.Combine(dataRoot, "app-data", "collaboard")));
    }

    [Fact]
    public void ResolvePath_ReturnsAbsolutePath()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "collabhost-appdata-absolute");

        var resolved = AppDataPathResolver.ResolvePath(dataRoot, "my-app");

        Path.IsPathRooted(resolved).ShouldBeTrue();
    }

    [Fact]
    public void ResolveFor_DelegatesToResolvePath_WithConstructedDataRoot()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "collabhost-appdata-instance");
        var resolver = new AppDataPathResolver(dataRoot);

        var resolved = resolver.ResolveFor("api-app");

        resolved.ShouldBe(AppDataPathResolver.ResolvePath(dataRoot, "api-app"));
    }

    [Fact]
    public void ResolveFor_DistinctSlugs_ProduceDistinctPaths()
    {
        var resolver = new AppDataPathResolver(Path.Combine(Path.GetTempPath(), "collabhost-appdata-distinct"));

        var first = resolver.ResolveFor("app-one");
        var second = resolver.ResolveFor("app-two");

        first.ShouldNotBe(second);
    }

    [Fact]
    public void Constructor_BlankDataDirectory_Throws() =>
        Should.Throw<ArgumentException>(() => new AppDataPathResolver("   "));

    [Fact]
    public void ResolvePath_BlankSlug_Throws() =>
        Should.Throw<ArgumentException>
        (
            () => AppDataPathResolver.ResolvePath(Path.GetTempPath(), "  ")
        );
}
