using Collabhost.Api.Platform;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Platform;

[Collection(nameof(StartupStderrTests))]
public class StartupStderrTests : IDisposable
{
    private readonly TextWriter _originalError;
    private readonly StringWriter _capture;

    public StartupStderrTests()
    {
        _originalError = Console.Error;
        _capture = new StringWriter();
        Console.SetError(_capture);
    }

    public void Dispose()
    {
        Console.SetError(_originalError);
        _capture.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Write_EmitsSummaryAndExitCode()
    {
        StartupStderr.Write
        (
            summary: "migration failed",
            details: [("Backup created at", "/tmp/backup.db")],
            recoverySteps: ["Restore the backup."],
            exitCode: 20
        );

        var output = _capture.ToString();

        output.ShouldContain("Collabhost startup failed: migration failed");
        output.ShouldContain("Details:");
        output.ShouldContain("Backup created at: /tmp/backup.db");
        output.ShouldContain("Recovery:");
        output.ShouldContain("1. Restore the backup.");
        output.ShouldContain("Exit code: 20");
    }

    [Fact]
    public void Write_WithNoDetailsOrRecovery_StillEmitsSummaryAndExitCode()
    {
        StartupStderr.Write
        (
            summary: "data directory not writable",
            details: [],
            recoverySteps: [],
            exitCode: 10
        );

        var output = _capture.ToString();

        output.ShouldContain("Collabhost startup failed: data directory not writable");
        output.ShouldContain("Exit code: 10");
        output.ShouldNotContain("Details:");
        output.ShouldNotContain("Recovery:");
    }

    [Fact]
    public void Write_NumbersRecoveryStepsSequentially()
    {
        StartupStderr.Write
        (
            summary: "x",
            details: [],
            recoverySteps: ["first step", "second step", "third step"],
            exitCode: 50
        );

        var output = _capture.ToString();

        output.ShouldContain("1. first step");
        output.ShouldContain("2. second step");
        output.ShouldContain("3. third step");
    }
}

[CollectionDefinition(nameof(StartupStderrTests), DisableParallelization = true)]
public class StartupStderrCollection { }
