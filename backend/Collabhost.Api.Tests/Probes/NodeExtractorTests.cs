using Collabhost.Api.Probes;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Probes;

public class NodeExtractorTests : IDisposable
{
    private readonly string _tempDir;

    public NodeExtractorTests()
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
    public void Extract_NoPackageJson_ReturnsNull()
    {
        var result = NodeExtractor.Extract(null, _tempDir);

        result.ShouldBeNull();
    }

    [Fact]
    public void Extract_NonexistentDirectory_ReturnsNull()
    {
        var result = NodeExtractor.Extract(null, Path.Combine(_tempDir, "nonexistent"));

        result.ShouldBeNull();
    }

    [Fact]
    public void Extract_BasicPackageJson_ParsesFields()
    {
        File.WriteAllText
        (
            Path.Combine(_tempDir, "package.json"),
            """
            {
              "name": "my-app",
              "version": "2.0.0",
              "type": "module",
              "engines": { "node": ">=22.0.0" },
              "packageManager": "pnpm@9.15.4",
              "dependencies": {
                "react": "^19.1.0",
                "react-dom": "^19.1.0"
              },
              "devDependencies": {
                "typescript": "5.8.3",
                "vite": "6.4.1"
              }
            }
            """
        );

        var result = NodeExtractor.Extract(null, _tempDir);

        result.ShouldNotBeNull();
        result.PackageJson.ShouldNotBeNull();
        result.PackageJson.Name.ShouldBe("my-app");
        result.PackageJson.Type.ShouldBe("module");
        result.PackageJson.EngineNode.ShouldBe(">=22.0.0");
        result.PackageJson.PackageManager.ShouldBe("pnpm@9.15.4");
        result.PackageJson.Dependencies.Count.ShouldBe(2);
        result.PackageJson.DevDependencies.Count.ShouldBe(2);
    }

    [Fact]
    public void Extract_DetectsLockfile_Pnpm()
    {
        File.WriteAllText(Path.Combine(_tempDir, "package.json"), """{"name":"test"}""");
        File.WriteAllText(Path.Combine(_tempDir, "pnpm-lock.yaml"), "lockfileVersion: 9");

        var result = NodeExtractor.Extract(null, _tempDir);

        result.ShouldNotBeNull();
        result.DetectedLockfile.ShouldBe("pnpm");
    }

    [Fact]
    public void Extract_DetectsLockfile_Npm()
    {
        File.WriteAllText(Path.Combine(_tempDir, "package.json"), """{"name":"test"}""");
        File.WriteAllText(Path.Combine(_tempDir, "package-lock.json"), "{}");

        var result = NodeExtractor.Extract(null, _tempDir);

        result.ShouldNotBeNull();
        result.DetectedLockfile.ShouldBe("npm");
    }

    [Fact]
    public void Extract_ProjectRootTakesPriority()
    {
        var projectRoot = Path.Combine(_tempDir, "source");
        var artifactDir = Path.Combine(_tempDir, "dist");

        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(artifactDir);

        File.WriteAllText
        (
            Path.Combine(projectRoot, "package.json"),
            """{"name":"from-project-root","dependencies":{"react":"19.0.0"}}"""
        );

        // Artifact dir has no package.json
        var result = NodeExtractor.Extract(projectRoot, artifactDir);

        result.ShouldNotBeNull();
        result.PackageJson.ShouldNotBeNull();
        result.PackageJson.Name.ShouldBe("from-project-root");
    }

    [Fact]
    public void Extract_FallsBackToArtifactDir_WhenProjectRootEmpty()
    {
        File.WriteAllText
        (
            Path.Combine(_tempDir, "package.json"),
            """{"name":"from-artifact"}"""
        );

        var result = NodeExtractor.Extract(null, _tempDir);

        result.ShouldNotBeNull();
        result.PackageJson.ShouldNotBeNull();
        result.PackageJson.Name.ShouldBe("from-artifact");
    }

    [Fact]
    public void Extract_FallsBackToArtifactDir_WhenProjectRootExistsButLacksPackageJson()
    {
        var projectRoot = Path.Combine(_tempDir, "src");
        var artifactDir = _tempDir;

        Directory.CreateDirectory(projectRoot);

        File.WriteAllText
        (
            Path.Combine(artifactDir, "package.json"),
            """{"name":"from-artifact","dependencies":{"react":"19.0.0"}}"""
        );

        var result = NodeExtractor.Extract(projectRoot, artifactDir);

        result.ShouldNotBeNull();
        result.PackageJson.ShouldNotBeNull();
        result.PackageJson.Name.ShouldBe("from-artifact");
    }

    [Fact]
    public void Extract_MalformedJson_ReturnsNull()
    {
        File.WriteAllText(Path.Combine(_tempDir, "package.json"), "not valid {");

        var result = NodeExtractor.Extract(null, _tempDir);

        result.ShouldBeNull();
    }

    [Fact]
    public void Extract_CommonJsDefault_WhenTypeAbsent()
    {
        File.WriteAllText(Path.Combine(_tempDir, "package.json"), """{"name":"test"}""");

        var result = NodeExtractor.Extract(null, _tempDir);

        result.ShouldNotBeNull();
        result.PackageJson.ShouldNotBeNull();
        result.PackageJson.Type.ShouldBeNull();
    }
}
