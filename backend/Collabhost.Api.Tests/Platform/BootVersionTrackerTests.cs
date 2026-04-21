using Collabhost.Api.Platform;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Platform;

public class BootVersionTrackerTests : IDisposable
{
    private readonly string _dataDirectory;

    public BootVersionTrackerTests()
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), $"collabhost-bvt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            try
            {
                Directory.Delete(_dataDirectory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Read_WhenFileMissing_ReturnsUnknown() =>
        BootVersionTracker.Read(_dataDirectory).ShouldBe(BootVersionTracker.UnknownVersion);

    [Fact]
    public void Read_WhenFileHasValidSemver_ReturnsIt()
    {
        var path = Path.Combine(_dataDirectory, BootVersionTracker.SentinelFileName);

        File.WriteAllText(path, "0.2.0\n");

        BootVersionTracker.Read(_dataDirectory).ShouldBe("0.2.0");
    }

    [Fact]
    public void Read_WhenFileHasLeadingV_ReturnsAsIs()
    {
        var path = Path.Combine(_dataDirectory, BootVersionTracker.SentinelFileName);

        File.WriteAllText(path, "v0.2.0");

        BootVersionTracker.Read(_dataDirectory).ShouldBe("v0.2.0");
    }

    [Fact]
    public void Read_WhenFileHasPrereleaseSuffix_ReturnsAsIs()
    {
        var path = Path.Combine(_dataDirectory, BootVersionTracker.SentinelFileName);

        File.WriteAllText(path, "0.2.0-rc.1\n");

        BootVersionTracker.Read(_dataDirectory).ShouldBe("0.2.0-rc.1");
    }

    [Fact]
    public void Read_WhenFileIsMalformed_ReturnsUnknown()
    {
        var path = Path.Combine(_dataDirectory, BootVersionTracker.SentinelFileName);

        File.WriteAllText(path, "not-a-version\n");

        BootVersionTracker.Read(_dataDirectory).ShouldBe(BootVersionTracker.UnknownVersion);
    }

    [Fact]
    public void Read_WhenFileIsEmpty_ReturnsUnknown()
    {
        var path = Path.Combine(_dataDirectory, BootVersionTracker.SentinelFileName);

        File.WriteAllText(path, string.Empty);

        BootVersionTracker.Read(_dataDirectory).ShouldBe(BootVersionTracker.UnknownVersion);
    }

    [Fact]
    public void Write_ThenRead_RoundTripsExactly()
    {
        BootVersionTracker.Write(_dataDirectory, "1.2.3");

        BootVersionTracker.Read(_dataDirectory).ShouldBe("1.2.3");
    }

    [Fact]
    public void Write_Overwrite_ReplacesPriorValue()
    {
        BootVersionTracker.Write(_dataDirectory, "0.1.0");
        BootVersionTracker.Write(_dataDirectory, "0.2.0");

        BootVersionTracker.Read(_dataDirectory).ShouldBe("0.2.0");
    }

    [Fact]
    public void Write_PersistsToSentinelFilename()
    {
        BootVersionTracker.Write(_dataDirectory, "0.5.0");

        var sentinelPath = Path.Combine(_dataDirectory, BootVersionTracker.SentinelFileName);

        File.Exists(sentinelPath).ShouldBeTrue();
    }
}
