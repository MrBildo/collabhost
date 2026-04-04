using Collabhost.Api.Registry;
using Collabhost.Api.Supervisor;
using Collabhost.Api.Supervisor.Containment;

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

        process.MarkCrashed(1);

        process.IsCrashed.ShouldBeTrue();
        process.HasMaxRestartsExceeded(10).ShouldBeFalse();
    }

    [Fact]
    public void HasMaxRestartsExceeded_ReturnsTrueAfterMaxCrashes()
    {
        var process = CreateProcess();

        for (var i = 0; i < 10; i++)
        {
            process.MarkCrashed(1);
        }

        process.HasMaxRestartsExceeded(10).ShouldBeTrue();
    }

    [Fact]
    public void GetBackoffDelay_FirstFailure_ReturnsOneSecond()
    {
        var process = CreateProcess();

        process.MarkCrashed(1);

        var delay = process.GetBackoffDelay();

        delay.ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void GetBackoffDelay_SecondFailure_ReturnsTwoSeconds()
    {
        var process = CreateProcess();

        process.MarkCrashed(1);
        process.MarkCrashed(1);

        var delay = process.GetBackoffDelay();

        delay.ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void GetBackoffDelay_CapsAtSixtySeconds()
    {
        var process = CreateProcess();

        for (var i = 0; i < 20; i++)
        {
            process.MarkCrashed(1);
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

        process.MarkCrashed(1);
        process.MarkCrashed(1);
        process.MarkCrashed(1);

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

    [Fact]
    public async Task AcquireOperationLockAsync_SecondCallBlocks_UntilFirstReleased()
    {
        var process = CreateProcess();
        var secondAcquired = false;

        var firstLock = await process.AcquireOperationLockAsync();

        var secondTask = Task.Run(async () =>
        {
            await using var secondLock = await process.AcquireOperationLockAsync();
            secondAcquired = true;
        });

        await Task.Delay(200);

        secondAcquired.ShouldBeFalse();

        await firstLock.DisposeAsync();

        await secondTask.WaitAsync(TimeSpan.FromSeconds(5));

        secondAcquired.ShouldBeTrue();
    }

    [Fact]
    public async Task AcquireOperationLockAsync_RespectsCancellation()
    {
        var process = CreateProcess();
        using var alreadyCancelled = new CancellationTokenSource();
        await alreadyCancelled.CancelAsync();

        await using var firstLock = await process.AcquireOperationLockAsync(CancellationToken.None);

        await Should.ThrowAsync<OperationCanceledException>(
            () => process.AcquireOperationLockAsync(alreadyCancelled.Token)
        );
    }

    [Fact]
    public async Task AcquireOperationLockAsync_DisposingRelease_AllowsNextAcquisition()
    {
        var process = CreateProcess();

        var firstLock = await process.AcquireOperationLockAsync();

        await firstLock.DisposeAsync();

        var secondTask = process.AcquireOperationLockAsync();

        var completed = await Task.WhenAny(secondTask, Task.Delay(TimeSpan.FromSeconds(5)));

        completed.ShouldBe(secondTask);

        await using var secondLock = await secondTask;
    }

    [Fact]
    public async Task Dispose_DisposesOperationLock()
    {
        var process = CreateProcess();

        process.Dispose();

        await Should.ThrowAsync<ObjectDisposedException>(
            () => process.AcquireOperationLockAsync()
        );
    }

    [Fact]
    public void Dispose_DisposesContainmentHandle()
    {
        var process = CreateProcess();
        var fakeHandle = new FakeContainmentHandle();

        process.SetContainmentHandle(fakeHandle);

        process.Dispose();

        fakeHandle.IsDisposed.ShouldBeTrue();
    }

    [Fact]
    public void SetContainmentHandle_AcceptsNull()
    {
        var process = CreateProcess();

        process.SetContainmentHandle(null);

        Should.NotThrow(() => process.Dispose());
    }

    [Fact]
    public void SetContainmentHandle_ReplacesExistingHandle()
    {
        var process = CreateProcess();
        var firstHandle = new FakeContainmentHandle();
        var secondHandle = new FakeContainmentHandle();

        process.SetContainmentHandle(firstHandle);
        process.SetContainmentHandle(secondHandle);

        process.Dispose();

        // Only the most recently set handle is disposed by the process
        firstHandle.IsDisposed.ShouldBeFalse();
        secondHandle.IsDisposed.ShouldBeTrue();
    }
    // --- Backoff and Fatal state tests ---

    [Fact]
    public void MarkBackoff_IncrementsStartupFailures()
    {
        var process = CreateProcess();

        process.MarkStarting();
        process.MarkBackoff(1);

        process.StartupFailures.ShouldBe(1);

        process.MarkStarting();
        process.MarkBackoff(1);

        process.StartupFailures.ShouldBe(2);
    }

    [Fact]
    public void MarkBackoff_RecordsExitCode()
    {
        var process = CreateProcess();

        process.MarkStarting();
        process.MarkBackoff(42);

        process.LastExitCode.ShouldBe(42);
        process.LastExitAt.ShouldNotBeNull();
    }

    [Fact]
    public void MarkFatal_SetsState()
    {
        var process = CreateProcess();

        process.MarkFatal();

        process.IsFatal.ShouldBeTrue();
        process.State.ShouldBe(ProcessState.Fatal);
    }

    [Fact]
    public void HasMaxStartupRetriesExceeded_ReturnsTrue_AfterMaxFailures()
    {
        var process = CreateProcess();

        for (var i = 0; i < 3; i++)
        {
            process.MarkStarting();
            process.MarkBackoff(1);
        }

        process.HasMaxStartupRetriesExceeded(3).ShouldBeTrue();
    }

    [Fact]
    public void GetStartupRetryDelay_ReturnsLinearDelay()
    {
        var process = CreateProcess();

        process.MarkStarting();
        process.MarkBackoff(1);

        process.GetStartupRetryDelay().ShouldBe(TimeSpan.FromSeconds(1));

        process.MarkStarting();
        process.MarkBackoff(1);

        process.GetStartupRetryDelay().ShouldBe(TimeSpan.FromSeconds(2));

        process.MarkStarting();
        process.MarkBackoff(1);

        process.GetStartupRetryDelay().ShouldBe(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void MarkRunning_ResetsStartupFailures()
    {
        var process = CreateProcess();
        var handle = new FakeProcessHandle();

        process.MarkStarting();
        process.MarkBackoff(1);
        process.MarkBackoff(1);

        process.StartupFailures.ShouldBe(2);

        process.MarkRunning(handle);

        process.StartupFailures.ShouldBe(0);
    }

    [Fact]
    public void MarkCrashed_RecordsExitCodeAndTime()
    {
        var process = CreateProcess();
        var handle = new FakeProcessHandle();

        process.MarkRunning(handle);

        var before = DateTime.UtcNow;

        process.MarkCrashed(137);

        process.LastExitCode.ShouldBe(137);
        process.LastExitAt.ShouldNotBeNull();
        process.LastExitAt!.Value.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public void MarkStopped_ResetsStartupFailures()
    {
        var process = CreateProcess();

        process.MarkStarting();
        process.MarkBackoff(1);
        process.MarkBackoff(1);

        process.StartupFailures.ShouldBe(2);

        process.MarkStopped();

        process.StartupFailures.ShouldBe(0);
    }

    [Fact]
    public void MarkBackoff_TransitionsFromStarting()
    {
        var process = CreateProcess();

        process.MarkStarting();

        var previous = process.MarkBackoff(1);

        previous.ShouldBe(ProcessState.Starting);
        process.IsBackoff.ShouldBeTrue();
    }

    [Fact]
    public void MarkFatal_OnlyExitsVia_MarkStarting()
    {
        var process = CreateProcess();

        process.MarkFatal();

        process.IsFatal.ShouldBeTrue();

        // MarkStarting is the exit path from Fatal (operator manual restart)
        var previous = process.MarkStarting();

        previous.ShouldBe(ProcessState.Fatal);
        process.State.ShouldBe(ProcessState.Starting);
    }
}

// No subclasses expected -- test fake for containment handle
file sealed class FakeContainmentHandle : IContainmentHandle
{
    public bool IsDisposed { get; private set; }

    public bool AssignProcess(int processId) => true;

    public void Terminate(uint exitCode) { }

    public void Dispose() => IsDisposed = true;
}

// No subclasses expected -- test fake for process handle
file sealed class FakeProcessHandle : IProcessHandle
{
    public int Pid => 12345;

    public bool HasExited => false;

    public int? ExitCode => null;

    public event Action<int>? Exited;

    public bool TryGracefulShutdown() => false;

    public void Kill() => _ = this;

    public void Dispose() =>
        // Suppress unused event warning
        _ = Exited;
}
