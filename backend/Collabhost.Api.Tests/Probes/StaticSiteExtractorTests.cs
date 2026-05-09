using Collabhost.Api.Probes;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Probes;

public class StaticSiteExtractorTests : IDisposable
{
    private readonly string _tempDir;

    public StaticSiteExtractorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "collabhost-static-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Extract_EmptyDirectory_ReturnsNull()
    {
        var result = StaticSiteExtractor.Extract(_tempDir);

        result.ShouldBeNull();
    }

    [Fact]
    public void Extract_NonExistentDirectory_ReturnsNull()
    {
        var result = StaticSiteExtractor.Extract(Path.Combine(_tempDir, "nope"));

        result.ShouldBeNull();
    }

    [Fact]
    public void Extract_IndexHtmlAtRoot_ReturnsHasIndexHtmlTrue()
    {
        File.WriteAllText(Path.Combine(_tempDir, "index.html"), "<html></html>");

        var result = StaticSiteExtractor.Extract(_tempDir);

        result.ShouldNotBeNull();
        result.HasIndexHtml.ShouldBeTrue();
        result.HtmlFileCount.ShouldBe(1);
    }

    [Fact]
    public void Extract_AssetsSubdirectory_ReturnsHasNestedAssetsTrue()
    {
        File.WriteAllText(Path.Combine(_tempDir, "index.html"), "<html></html>");
        Directory.CreateDirectory(Path.Combine(_tempDir, "assets"));
        File.WriteAllText(Path.Combine(_tempDir, "assets", "main.js"), "console.log(1);");

        var result = StaticSiteExtractor.Extract(_tempDir);

        result.ShouldNotBeNull();
        result.HasNestedAssets.ShouldBeTrue();
    }

    [Fact]
    public void Extract_TalliesAssetBytesIncludingNested()
    {
        File.WriteAllText(Path.Combine(_tempDir, "index.html"), new string('a', 100));
        Directory.CreateDirectory(Path.Combine(_tempDir, "assets"));
        File.WriteAllText(Path.Combine(_tempDir, "assets", "bundle.js"), new string('b', 250));

        var result = StaticSiteExtractor.Extract(_tempDir);

        result.ShouldNotBeNull();
        result.TotalAssetBytes.ShouldBe(350);
    }

    [Fact]
    public void Extract_HtmlOnlyNoIndex_StillReturnsRecord()
    {
        File.WriteAllText(Path.Combine(_tempDir, "page.html"), "<html></html>");

        var result = StaticSiteExtractor.Extract(_tempDir);

        result.ShouldNotBeNull();
        result.HasIndexHtml.ShouldBeFalse();
        result.HtmlFileCount.ShouldBe(1);
    }
}
