using System.Diagnostics;

namespace Collabhost.Api.Platform;

// Singleton source-of-truth for platform (this-process) start time. Registered by
// PlatformRegistration.AddPlatform().
//
// The epoch is the OS-recorded start time of THIS process (Process.StartTime), captured by
// the kernel at exec -- strictly before Main, before DI, before any managed child is spawned.
// That ordering gives the #408 invariant for free: a supervised child's _startedAt (set at
// MarkRunning, which runs after Main) is always >= this value, so platform uptime is always
// >= any managed app's uptime. The kernel start time also exists the moment the process does,
// so it never reads 0 / DateTime.MinValue and never races a callback.
//
// This subsumes the #222 race: the prior source (IHostApplicationLifetime.ApplicationStarted)
// fired AFTER process start and so under-reported uptime, and could read younger than a
// box-boot-started managed app (the live 3.07d < 3.76d defect Theo observed). StartTime is
// computed once at construction; the singleton lifetime makes it a stable snapshot.
public sealed class ApplicationStartTime : IApplicationStartTime
{
    public DateTime UtcStartedAt { get; } = Process.GetCurrentProcess().StartTime.ToUniversalTime();
}
