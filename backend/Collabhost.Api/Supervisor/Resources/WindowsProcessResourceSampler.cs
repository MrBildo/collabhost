using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Collabhost.Api.Supervisor.Resources;

[SupportedOSPlatform("windows")]
public class WindowsProcessResourceSampler(ILogger<WindowsProcessResourceSampler> logger)
    : IProcessResourceSampler
{
    private readonly ILogger<WindowsProcessResourceSampler> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    private readonly ConcurrentDictionary<int, CpuSample> _previousSamples = new();

    public ProcessResourceSnapshot? Sample(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);

            // Refresh ensures the cached property values reflect the current process state.
            // Without this, WorkingSet64 / TotalProcessorTime can return stale values from
            // when the Process object was first created.
            process.Refresh();

            var memoryBytes = process.WorkingSet64;
            var memoryMb = memoryBytes / (1024.0 * 1024.0);

            var cpuTime = process.TotalProcessorTime;
            var sampledAt = DateTime.UtcNow;

            double? cpuPercent = null;

            if (_previousSamples.TryGetValue(pid, out var previous))
            {
                var wallClockDelta = (sampledAt - previous.SampledAt).TotalMilliseconds;
                var cpuDelta = (cpuTime - previous.CpuTime).TotalMilliseconds;

                if (wallClockDelta > 0)
                {
                    // CPU% across all cores. A value above 100% means the process
                    // used more than one core's worth of CPU time during the interval.
                    cpuPercent = Math.Max(0, cpuDelta / wallClockDelta * 100.0);
                }
            }

            _previousSamples[pid] = new CpuSample(cpuTime, sampledAt);

            int? handleCount = null;

            try
            {
                handleCount = process.HandleCount;
            }
            catch (InvalidOperationException)
            {
                // Process has exited between the GetProcessById call and the HandleCount
                // read. The other fields are still valid for this snapshot.
            }

            return new ProcessResourceSnapshot(cpuPercent, memoryMb, handleCount, sampledAt);
        }
        catch (ArgumentException)
        {
            // Process not found -- already exited.
            _previousSamples.TryRemove(pid, out _);
            return null;
        }
        catch (InvalidOperationException)
        {
            // Process has exited between GetProcessById and reading the properties.
            _previousSamples.TryRemove(pid, out _);
            return null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _logger.LogDebug(exception, "Failed to sample resources for PID {Pid}", pid);
            return null;
        }
    }

    public void Forget(int pid) => _previousSamples.TryRemove(pid, out _);

    private record struct CpuSample(TimeSpan CpuTime, DateTime SampledAt);
}
