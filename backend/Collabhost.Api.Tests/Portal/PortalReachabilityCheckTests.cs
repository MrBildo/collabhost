using Collabhost.Api.Portal;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Portal;

public class PortalReachabilityCheckTests : IDisposable
{
    private readonly string _baseDirectory;

    public PortalReachabilityCheckTests()
    {
        _baseDirectory = Path.Combine
        (
            Path.GetTempPath(), "collabhost-reachability-tests", Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(_baseDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_baseDirectory))
        {
            Directory.Delete(_baseDirectory, true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Validate_NoWwwroot_ReportsMissing()
    {
        var outcome = PortalReachabilityCheck.Validate(_baseDirectory, NullLogger.Instance);

        outcome.Status.ShouldBe(PortalReachabilityStatus.Missing);
        outcome.WwwrootPath.ShouldBe(Path.Combine(_baseDirectory, "wwwroot"));
        outcome.RecoverySteps.ShouldNotBeEmpty();
    }

    [Fact]
    public void Validate_WwwrootButNoIndexHtml_ReportsMissing()
    {
        Directory.CreateDirectory(Path.Combine(_baseDirectory, "wwwroot", "assets"));
        File.WriteAllText(Path.Combine(_baseDirectory, "wwwroot", "assets", "x.js"), "//");

        var outcome = PortalReachabilityCheck.Validate(_baseDirectory, NullLogger.Instance);

        outcome.Status.ShouldBe(PortalReachabilityStatus.Missing);
    }

    [Fact]
    public void Validate_AssetsDirectoryEmpty_ReportsAssetsEmpty()
    {
        Directory.CreateDirectory(Path.Combine(_baseDirectory, "wwwroot", "assets"));
        File.WriteAllText(Path.Combine(_baseDirectory, "wwwroot", "index.html"), "<html/>");

        var outcome = PortalReachabilityCheck.Validate(_baseDirectory, NullLogger.Instance);

        outcome.Status.ShouldBe(PortalReachabilityStatus.AssetsEmpty);
        outcome.RecoverySteps.ShouldNotBeEmpty();
    }

    [Fact]
    public void Validate_NoAssetsDirectory_ReportsAssetsEmpty()
    {
        Directory.CreateDirectory(Path.Combine(_baseDirectory, "wwwroot"));
        File.WriteAllText(Path.Combine(_baseDirectory, "wwwroot", "index.html"), "<html/>");

        var outcome = PortalReachabilityCheck.Validate(_baseDirectory, NullLogger.Instance);

        outcome.Status.ShouldBe(PortalReachabilityStatus.AssetsEmpty);
    }

    [Fact]
    public void Validate_FullyPopulated_ReportsOk()
    {
        Directory.CreateDirectory(Path.Combine(_baseDirectory, "wwwroot", "assets"));
        File.WriteAllText(Path.Combine(_baseDirectory, "wwwroot", "index.html"), "<html/>");
        File.WriteAllText(Path.Combine(_baseDirectory, "wwwroot", "assets", "x.js"), "//");

        var outcome = PortalReachabilityCheck.Validate(_baseDirectory, NullLogger.Instance);

        outcome.Status.ShouldBe(PortalReachabilityStatus.Ok);
        outcome.RecoverySteps.ShouldBeEmpty();
    }
}
