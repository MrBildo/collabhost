using Collabhost.Api.Registry;
using Collabhost.Api.Supervisor;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Supervisor;

public class ManagedProcessTests
{
    private static ManagedProcess CreateProcess() =>
        new(Ulid.NewUlid(), "test-app", "Test App");

    [Fact]
    public void NewProcess_StateIsStopped()
    {
        var process = CreateProcess();

        process.State.ShouldBe(ProcessState.Stopped);
        process.IsStopped.ShouldBeTrue();
    }

    [Fact]
    public void MarkStarting_TransitionsFromStopped()
    {
        var process = CreateProcess();

        var previous = process.MarkStarting();

        previous.ShouldBe(ProcessState.Stopped);
        process.State.ShouldBe(ProcessState.Starting);
    }

    [Fact]
    public void MarkCrashed_IncrementsConsecutiveFailures()
    {
        var process = CreateProcess();

        process.MarkCrashed();

        process.IsCrashed.ShouldBeTrue();
        process.HasMaxRestartsExceeded(10).ShouldBeFalse();
    }

    [Fact]
    public void HasMaxRestartsExceeded_ReturnsTrueAfterMaxCrashes()
    {
        var process = CreateProcess();

        for (var i = 0; i < 10; i++)
        {
            process.MarkCrashed();
        }

        process.HasMaxRestartsExceeded(10).ShouldBeTrue();
    }

    [Fact]
    public void GetBackoffDelay_FirstFailure_ReturnsOneSecond()
    {
        var process = CreateProcess();

        process.MarkCrashed();

        var delay = process.GetBackoffDelay();

        delay.ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void GetBackoffDelay_SecondFailure_ReturnsTwoSeconds()
    {
        var process = CreateProcess();

        process.MarkCrashed();
        process.MarkCrashed();

        var delay = process.GetBackoffDelay();

        delay.ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void GetBackoffDelay_CapsAtSixtySeconds()
    {
        var process = CreateProcess();

        for (var i = 0; i < 20; i++)
        {
            process.MarkCrashed();
        }

        var delay = process.GetBackoffDelay();

        delay.ShouldBe(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void MarkRestarting_IncrementsRestartCount()
    {
        var process = CreateProcess();

        process.MarkRestarting();

        process.RestartCount.ShouldBe(1);
        process.IsRestarting.ShouldBeTrue();
        process.LastRestartAt.ShouldNotBeNull();
    }

    [Fact]
    public void MarkStopped_ResetsState()
    {
        var process = CreateProcess();

        process.MarkStarting();
        process.MarkStopped();

        process.IsStopped.ShouldBeTrue();
        process.Pid.ShouldBeNull();
        process.Port.ShouldBeNull();
        process.StartedAt.ShouldBeNull();
    }

    [Fact]
    public void AssignPort_SetsPort()
    {
        var process = CreateProcess();

        process.AssignPort(8080);

        process.Port.ShouldBe(8080);
    }

    [Fact]
    public void StoppedByOperator_DefaultsFalse()
    {
        var process = CreateProcess();

        process.StoppedByOperator.ShouldBeFalse();
    }

    [Fact]
    public void MarkStoppedByOperator_SetsFlag()
    {
        var process = CreateProcess();

        process.MarkStoppedByOperator();

        process.StoppedByOperator.ShouldBeTrue();
    }

    [Fact]
    public void ClearStoppedByOperator_ClearsFlag()
    {
        var process = CreateProcess();

        process.MarkStoppedByOperator();
        process.ClearStoppedByOperator();

        process.StoppedByOperator.ShouldBeFalse();
    }

    [Fact]
    public void UptimeSeconds_WhenNotRunning_ReturnsNull()
    {
        var process = CreateProcess();

        process.UptimeSeconds.ShouldBeNull();
    }

    [Fact]
    public void LogBuffer_HasDefaultCapacity()
    {
        var process = CreateProcess();

        process.LogBuffer.Capacity.ShouldBe(1000);
    }

    [Fact]
    public void HasProcessExited_NoHandle_ReturnsTrue()
    {
        var process = CreateProcess();

        process.HasProcessExited.ShouldBeTrue();
    }

    [Fact]
    public void ResetRestartCount_ClearsFailureCounter()
    {
        var process = CreateProcess();

        process.MarkCrashed();
        process.MarkCrashed();
        process.MarkCrashed();

        process.ResetRestartCount();

        process.HasMaxRestartsExceeded(3).ShouldBeFalse();
    }

    [Fact]
    public void MarkStopping_CancelsPendingRestart()
    {
        var process = CreateProcess();
        var cancellation = new CancellationTokenSource();

        process.SetRestartDelayCancellation(cancellation);

        process.MarkStopping();

        cancellation.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public void MarkStopped_CancelsPendingRestart()
    {
        var process = CreateProcess();
        var cancellation = new CancellationTokenSource();

        process.SetRestartDelayCancellation(cancellation);

        process.MarkStopped();

        cancellation.IsCancellationRequested.ShouldBeTrue();
    }
}
