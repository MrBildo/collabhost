using Collabhost.Api.Probes;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Probes;

public class TypeScriptExtractorTests : IDisposable
{
    private readonly string _tempDir;

    public TypeScriptExtractorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "collabhost-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Extract_NoTsConfig_ReturnsNull()
    {
        var result = TypeScriptExtractor.Extract(null, null, _tempDir);

        result.ShouldBeNull();
    }

    [Fact]
    public void Extract_WithTsConfig_ParsesCompilerOptions()
    {
        File.WriteAllText
        (
            Path.Combine(_tempDir, "tsconfig.json"),
            """
            {
              "compilerOptions": {
                "strict": true,
                "target": "ES2022",
                "module": "ESNext"
              }
            }
            """
        );

        var packageJson = new RawPackageJson
        (
            "test", null, null, null, null,
            [],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["typescript"] = "5.8.3" }
        );

        var result = TypeScriptExtractor.Extract(packageJson, null, _tempDir);

        result.ShouldNotBeNull();
        result.Version.ShouldBe("5.8.3");
        result.TsConfig.ShouldNotBeNull();
        result.TsConfig.Strict.ShouldBe(true);
        result.TsConfig.Target.ShouldBe("ES2022");
        result.TsConfig.Module.ShouldBe("ESNext");
    }

    [Fact]
    public void Extract_StrictFalseByDefault_WhenNotSpecified()
    {
        File.WriteAllText
        (
            Path.Combine(_tempDir, "tsconfig.json"),
            """{"compilerOptions":{"target":"ES2020"}}"""
        );

        var result = TypeScriptExtractor.Extract(null, null, _tempDir);

        result.ShouldNotBeNull();
        result.TsConfig.ShouldNotBeNull();
        result.TsConfig.Strict.ShouldBe(false);
    }

    [Fact]
    public void Extract_TsConfigWithComments_ParsesSuccessfully()
    {
        File.WriteAllText
        (
            Path.Combine(_tempDir, "tsconfig.json"),
            """
            {
              // Compiler options
              "compilerOptions": {
                "strict": true,
                "target": "ESNext",
              }
            }
            """
        );

        var result = TypeScriptExtractor.Extract(null, null, _tempDir);

        result.ShouldNotBeNull();
        result.TsConfig.ShouldNotBeNull();
        result.TsConfig.Strict.ShouldBe(true);
    }

    [Fact]
    public void Extract_VersionFromPackageJson()
    {
        File.WriteAllText(Path.Combine(_tempDir, "tsconfig.json"), """{}""");

        var packageJson = new RawPackageJson
        (
            "test", null, null, null, null,
            [],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["typescript"] = "5.7.0" }
        );

        var result = TypeScriptExtractor.Extract(packageJson, null, _tempDir);

        result.ShouldNotBeNull();
        result.Version.ShouldBe("5.7.0");
    }

    [Fact]
    public void Extract_NullVersion_WhenNotInDevDependencies()
    {
        File.WriteAllText(Path.Combine(_tempDir, "tsconfig.json"), """{}""");

        var result = TypeScriptExtractor.Extract(null, null, _tempDir);

        result.ShouldNotBeNull();
        result.Version.ShouldBeNull();
    }

    [Fact]
    public void Extract_ProjectRoot_TakesPriority()
    {
        var projectRoot = Path.Combine(_tempDir, "source");
        var artifactDir = Path.Combine(_tempDir, "dist");

        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(artifactDir);

        File.WriteAllText
        (
            Path.Combine(projectRoot, "tsconfig.json"),
            """{"compilerOptions":{"strict":true}}"""
        );

        var result = TypeScriptExtractor.Extract(null, projectRoot, artifactDir);

        result.ShouldNotBeNull();
        result.TsConfig.ShouldNotBeNull();
        result.TsConfig.Strict.ShouldBe(true);
    }
}
