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

    // NOTE (SUP-01, #424): the five tests formerly here exercised an operation lock that lived
    // ON ManagedProcess (AcquireOperationLockAsync + Dispose_DisposesOperationLock). That lock was
    // the ROOT of SUP-01 -- it could never span a start/restart because those operations dispose
    // and replace the very ManagedProcess the lock lived on. The fix relocates the operation lock
    // to the supervisor (keyed by the stable appId), so the per-ManagedProcess lock no longer
    // exists. The relocated lock's mutual-exclusion / serialization semantics are now proven
    // end-to-end in ProcessSupervisorConcurrencyTests (no-double-spawn, serialize-across-op).

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

    // --- Reconciliation property tests ---

    [Fact]
    public void HandleExitCode_NoHandle_ReturnsNull()
    {
        var process = CreateProcess();

        process.HandleExitCode.ShouldBeNull();
    }

    [Fact]
    public void HandleExitCode_ProcessExited_ReturnsExitCode()
    {
        var process = CreateProcess();
        var handle = new FakeExitedProcessHandle(42);

        process.SetHandle(handle);

        process.HandleExitCode.ShouldBe(42);
    }

    [Fact]
    public void HasProcessExited_ProcessStillRunning_ReturnsFalse()
    {
        var process = CreateProcess();
        var handle = new FakeProcessHandle();

        process.SetHandle(handle);

        process.HasProcessExited.ShouldBeFalse();
    }

    [Fact]
    public void HasProcessExited_ProcessExited_ReturnsTrue()
    {
        var process = CreateProcess();
        var handle = new FakeExitedProcessHandle(0);

        process.SetHandle(handle);

        process.HasProcessExited.ShouldBeTrue();
    }

    [Fact]
    public void ReconciliationCondition_RunningButExited_Detected()
    {
        var process = CreateProcess();
        var handle = new FakeExitedProcessHandle(137);

        process.MarkStarting();
        process.SetHandle(handle);

        // Simulate passing startup grace period by marking Running
        process.MarkRunning();

        // The reconciliation condition: Running + HasProcessExited
        process.IsRunning.ShouldBeTrue();
        process.HasProcessExited.ShouldBeTrue();
        process.HandleExitCode.ShouldBe(137);
    }

    [Fact]
    public void ReconciliationCondition_RunningAndAlive_NotDetected()
    {
        var process = CreateProcess();
        var handle = new FakeProcessHandle();

        process.MarkStarting();
        process.SetHandle(handle);
        process.MarkRunning();

        // Running process that is still alive -- reconciliation should not trigger
        process.IsRunning.ShouldBeTrue();
        process.HasProcessExited.ShouldBeFalse();
    }

    // Card #428: the detail-builder derives uptime from the snapshot, so uptime is part of
    // the same coherent moment as the running state -- a running snapshot carries a non-null
    // uptime and a stopped one carries null, mirroring the UptimeSeconds property rule
    // (live only while running) but read under the one lock guarding state, pid, and port.
    [Fact]
    public void ReadSnapshot_Running_CarriesUptime()
    {
        var process = CreateProcess();

        process.AssignPort(8080);
        process.MarkRunning(new FakeProcessHandle());

        var snapshot = process.ReadSnapshot();

        snapshot.State.ShouldBe(ProcessState.Running);
        snapshot.UptimeSeconds.ShouldNotBeNull();
        snapshot.UptimeSeconds!.Value.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void ReadSnapshot_Stopped_UptimeIsNull()
    {
        var process = CreateProcess();

        process.AssignPort(8080);
        process.MarkRunning(new FakeProcessHandle());
        process.MarkStopped();

        var snapshot = process.ReadSnapshot();

        snapshot.State.ShouldBe(ProcessState.Stopped);
        snapshot.UptimeSeconds.ShouldBeNull();
    }

    // SUP-16: ManagedProcess state fields are mutated from multiple threads (operator
    // request threads, the crash-restart Task.Run closures, the grace-period closure,
    // the synchronous Exited callback) and read concurrently by request threads that
    // build the app-detail response. Each MarkXxx writes a GROUP of related fields
    // (MarkRunning sets Pid + Port-bearing handle + State; MarkStopped sets State then
    // nulls Pid + Port) and the reader assembles (State, Pid, Port) from independent
    // loads. Without a lock making the write-group atomic AND the read coherent, a
    // reader can stitch State==Running (one writer's flip) with Pid==null (a concurrent
    // writer's null) into one snapshot -- the torn read SUP-16 names.
    //
    // The invariant under test is the running-process coherence rule: a process the
    // snapshot reports as Running ALWAYS carries a PID and a port. ReadSnapshot reads
    // the three fields together under the same lock that guards the writers, so the
    // reader observes one coherent moment, never a half-applied transition.
    //
    // RED-first: against the pre-fix non-volatile auto-props (no lock, three separate
    // loads) the concurrent loop reliably catches a Running+null tear within the
    // iteration budget; post-fix the locked snapshot makes the tear unrepresentable.
    [Fact]
    public async Task ReadSnapshot_ConcurrentRunningStoppedFlips_NeverObservesRunningWithoutPidAndPort()
    {
        var process = CreateProcess();

        // A running handle always exposes a PID; the port is assigned alongside it,
        // mirroring StartAppInternalAsync's MarkRunning + AssignPort pairing.
        var runningHandle = new FakeProcessHandle();

        const int iterations = 200_000;

        using var start = new ManualResetEventSlim(false);
        var tornObservations = 0;

        // Writer: flip Running <-> Stopped as fast as possible. MarkRunning(handle)
        // sets the PID-bearing handle and Port then State; MarkStopped sets State then
        // nulls Pid and Port. The two together open the torn window for the reader.
        var writer = Task.Run
        (
            () =>
            {
                start.Wait();

                for (var i = 0; i < iterations; i++)
                {
                    process.AssignPort(8080);
                    process.MarkRunning(runningHandle);
                    process.MarkStopped();
                }
            }
        );

        // Reader: snapshot (State, Pid, Port) coherently and assert the running-process
        // invariant. A single torn observation fails the test.
        var reader = Task.Run
        (
            () =>
            {
                start.Wait();

                for (var i = 0; i < iterations; i++)
                {
                    var snapshot = process.ReadSnapshot();

                    // Running coherence (SUP-16 + #428): a Running snapshot always carries a
                    // PID, a port, AND an uptime -- all read under the one lock. A torn read
                    // would stitch Running with any of the three null.
                    if (snapshot.State == ProcessState.Running
                        && (snapshot.Pid is null || snapshot.Port is null || snapshot.UptimeSeconds is null))
                    {
                        Interlocked.Increment(ref tornObservations);
                    }
                }
            }
        );

        start.Set();

        await Task.WhenAll(writer, reader);

        tornObservations.ShouldBe(0);
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

// No subclasses expected -- test fake for a process handle that has already exited
file sealed class FakeExitedProcessHandle(int exitCode) : IProcessHandle
{
    public int Pid => 99999;

    public bool HasExited => true;

    public int? ExitCode => exitCode;

    public event Action<int>? Exited;

    public bool TryGracefulShutdown() => false;

    public void Kill() => _ = this;

    public void Dispose() =>
        // Suppress unused event warning
        _ = Exited;
}
