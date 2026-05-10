using Collabhost.Api.Platform;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Platform;

public class StartupPreflightTests : IDisposable
{
    private readonly string _rootDirectory;

    public StartupPreflightTests()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), $"collabhost-preflight-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            try
            {
                Directory.Delete(_rootDirectory, true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Validate_CreatesMissingDataDirectory_Succeeds()
    {
        Directory.Exists(_rootDirectory).ShouldBeFalse();

        var result = StartupPreflight.Validate(_rootDirectory, AppContext.BaseDirectory);

        result.Success.ShouldBeTrue();
        Directory.Exists(_rootDirectory).ShouldBeTrue();
    }

    [Fact]
    public void Validate_CreatesBackupsSubdirectory_Succeeds()
    {
        var result = StartupPreflight.Validate(_rootDirectory, AppContext.BaseDirectory);

        result.Success.ShouldBeTrue();
        result.BackupsDirectory.ShouldNotBeNull();
        Directory.Exists(result.BackupsDirectory).ShouldBeTrue();
        Path.GetFileName(result.BackupsDirectory).ShouldBe(StartupPreflight.BackupsSubdirectory);
    }

    [Fact]
    public void Validate_ReturnsResolvedDataDirectoryOnSuccess()
    {
        var result = StartupPreflight.Validate(_rootDirectory, AppContext.BaseDirectory);

        result.Success.ShouldBeTrue();
        result.DataDirectory.ShouldBe(_rootDirectory);
    }

    [Fact]
    public void Validate_ExistingWritableDirectory_Succeeds()
    {
        Directory.CreateDirectory(_rootDirectory);

        var result = StartupPreflight.Validate(_rootDirectory, AppContext.BaseDirectory);

        result.Success.ShouldBeTrue();
    }

    [Fact]
    public void Validate_DoesNotLeaveSentinelFileBehind()
    {
        var result = StartupPreflight.Validate(_rootDirectory, AppContext.BaseDirectory);

        result.Success.ShouldBeTrue();

        // No stray preflight-sentinel file should remain
        Directory.EnumerateFiles(_rootDirectory, ".preflight-sentinel")
            .ShouldBeEmpty();
    }

    [Fact]
    public void Validate_PathPointingToExistingFile_Fails()
    {
        // Create a file at the data-directory path to provoke a non-writable condition.
        Directory.CreateDirectory(Path.GetDirectoryName(_rootDirectory)!);
        File.WriteAllText(_rootDirectory, "not a directory");

        try
        {
            var result = StartupPreflight.Validate(_rootDirectory, AppContext.BaseDirectory);

            result.Success.ShouldBeFalse();
            result.FailureSummary.ShouldNotBeNullOrWhiteSpace();
            result.RecoverySteps.ShouldNotBeEmpty();
        }
        finally
        {
            File.Delete(_rootDirectory);
        }
    }

    [Fact]
    public void Validate_ThrowsOnEmptyDataDirectory() =>
        Should.Throw<ArgumentException>(() => StartupPreflight.Validate(string.Empty, AppContext.BaseDirectory));

    [Fact]
    public void Validate_ThrowsOnEmptyContentRoot() =>
        Should.Throw<ArgumentException>(() => StartupPreflight.Validate(_rootDirectory, string.Empty));

    [Fact]
    public void Validate_MissingContentRoot_Fails()
    {
        // Card #247: the packaging-sanity check now anchors on the ContentRootPath instead
        // of AppContext.BaseDirectory. A non-existent content root must surface as a fail
        // result with a recovery hint -- this is what an operator sees if a system-install
        // layout points ASPNETCORE_CONTENTROOT at a path that the unit didn't create.
        var bogusContentRoot = Path.Combine(Path.GetTempPath(), $"collabhost-bogus-{Guid.NewGuid():N}");

        Directory.Exists(bogusContentRoot).ShouldBeFalse();

        var result = StartupPreflight.Validate(_rootDirectory, bogusContentRoot);

        result.Success.ShouldBeFalse();
        result.FailureSummary.ShouldNotBeNull();
        result.FailureSummary.ShouldContain("content root");
        result.FailureDetails.ShouldContain(detail => detail.Value == bogusContentRoot);
    }

    [Fact]
    public void Validate_CustomContentRoot_Succeeds()
    {
        // Card #247: validate the env-var-set posture (system-install layout) where the
        // host's ContentRootPath diverges from AppContext.BaseDirectory.
        var contentRoot = Path.Combine(Path.GetTempPath(), $"collabhost-cr-{Guid.NewGuid():N}");
        Directory.CreateDirectory(contentRoot);

        try
        {
            var result = StartupPreflight.Validate(_rootDirectory, contentRoot);

            result.Success.ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public void Validate_CreatesMissingUserTypesDirectory_FlagsAsCreated()
    {
        var userTypesDir = Path.Combine(_rootDirectory, "user-types");

        var result = StartupPreflight.Validate(_rootDirectory, AppContext.BaseDirectory, userTypesDir);

        result.Success.ShouldBeTrue();
        result.UserTypesDirectory.ShouldBe(userTypesDir);
        result.UserTypesDirectoryCreated.ShouldBeTrue();
        Directory.Exists(userTypesDir).ShouldBeTrue();
    }

    [Fact]
    public void Validate_ExistingUserTypesDirectory_NotFlaggedAsCreated()
    {
        var userTypesDir = Path.Combine(_rootDirectory, "user-types");
        Directory.CreateDirectory(userTypesDir);

        var result = StartupPreflight.Validate(_rootDirectory, AppContext.BaseDirectory, userTypesDir);

        result.Success.ShouldBeTrue();
        result.UserTypesDirectoryCreated.ShouldBeFalse();
    }

    [Fact]
    public void Validate_NullUserTypesDirectory_SkipsUserTypesCheck()
    {
        var result = StartupPreflight.Validate(_rootDirectory, AppContext.BaseDirectory, userTypesDirectory: null);

        result.Success.ShouldBeTrue();
        result.UserTypesDirectory.ShouldBeNull();
        result.UserTypesDirectoryCreated.ShouldBeFalse();
    }
}
