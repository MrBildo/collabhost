namespace Collabhost.Api.Supervisor.Resources;

// CPU% requires two samples to compute a delta. Implementations hold per-PID state
// internally. The first call for a PID always returns CpuPercent = null (no prior
// sample to delta against); subsequent calls compute the percentage against the
// elapsed wall-clock time since the previous sample.
public interface IProcessResourceSampler
{
    ProcessResourceSnapshot? Sample(int pid);

    // Drop per-PID state for a process that has stopped. Prevents the per-PID
    // delta cache from leaking when apps stop/restart with new PIDs.
    void Forget(int pid);
}

public record ProcessResourceSnapshot
(
    double? CpuPercent,
    double MemoryMb,
    int? HandleCount,
    DateTime SampledAt
);
