using System.Diagnostics;

using Collabhost.Api.Platform;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Platform;

// Card #408: uptimeSeconds must track real platform (this-process) uptime -- monotonic,
// never 0 once running, and ALWAYS >= any managed-app process uptime (a supervised child
// cannot be older than the platform supervising it). The source-of-truth is the OS process
// start time (Process.StartTime), captured by the kernel at exec -- strictly before Main,
// before DI, before any child is spawned. This subsumes the #222 race (ApplicationStarted
// fired after process start, under-reporting uptime); the kernel start time is the true
// earliest-possible epoch.
public class ApplicationStartTimeTests
{
    [Fact]
    public void UtcStartedAt_ReflectsOsProcessStart()
    {
        // The platform-uptime epoch is the OS-recorded start time of THIS process.
        var expected = Process.GetCurrentProcess().StartTime.ToUniversalTime();

        var sut = new ApplicationStartTime();

        // Equal to the kernel's process start (within a second of conversion rounding).
        sut.UtcStartedAt.ShouldBe(expected, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void UtcStartedAt_KindIsUtc()
    {
        var sut = new ApplicationStartTime();

        sut.UtcStartedAt.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public void UtcStartedAt_IsAtOrBeforeNow_AndNotZero()
    {
        // The process exists, so its start time is a real moment in the past -- never 0,
        // never DateTime.MinValue, never in the future.
        var sut = new ApplicationStartTime();

        sut.UtcStartedAt.ShouldBeLessThanOrEqualTo(DateTime.UtcNow);
        sut.UtcStartedAt.ShouldBeGreaterThan(DateTime.UnixEpoch, "process start must be a real recent time, not 0/MinValue");
    }

    [Fact]
    public void PlatformUptime_IsNeverLessThanAManagedAppStartedRightNow()
    {
        // The #408 invariant. A managed app's _startedAt is DateTime.UtcNow captured at
        // MarkRunning -- which always runs AFTER this process started (Main -> DI ->
        // auto-start -> MarkRunning). So the platform epoch must be <= any app's epoch,
        // i.e. platform uptime >= app uptime, for an app started at the earliest a child
        // could be (right now). The old ApplicationStarted-callback source could read younger
        // than a box-boot-started child (the live 3.07d < 3.76d defect Theo observed).
        var sut = new ApplicationStartTime();

        // Earliest a supervised child's _startedAt could be: now (it cannot predate this read).
        var managedAppStartedAt = DateTime.UtcNow;

        var platformUptimeSeconds = (DateTime.UtcNow - sut.UtcStartedAt).TotalSeconds;
        var managedAppUptimeSeconds = (DateTime.UtcNow - managedAppStartedAt).TotalSeconds;

        platformUptimeSeconds.ShouldBeGreaterThanOrEqualTo
        (
            managedAppUptimeSeconds,
            "platform uptime must never be less than a managed app's uptime"
        );
    }
}
