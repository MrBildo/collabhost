using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.Versioning;

namespace Collabhost.Api.Supervisor.Resources;

[SupportedOSPlatform("linux")]
public class LinuxProcessResourceSampler(ILogger<LinuxProcessResourceSampler> logger)
    : IProcessResourceSampler
{
    private readonly ILogger<LinuxProcessResourceSampler> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    private readonly ConcurrentDictionary<int, CpuSample> _previousSamples = new();

    // sysconf(_SC_CLK_TCK) is conventionally 100 on Linux -- jiffies per second.
    // The kernel's USER_HZ has been 100 on x86/x86_64 for ~20 years; ARM follows.
    // Reading this dynamically would require P/Invoke into libc; the constant is
    // accurate for every distribution Collabhost is expected to run on.
    private const double _jiffiesPerSecond = 100.0;

    public ProcessResourceSnapshot? Sample(int pid)
    {
        try
        {
            var memoryMb = ReadMemoryMb(pid);

            if (memoryMb is null)
            {
                _previousSamples.TryRemove(pid, out _);
                return null;
            }

            var cpuJiffies = ReadCpuJiffies(pid);
            var sampledAt = DateTime.UtcNow;

            double? cpuPercent = null;

            if (cpuJiffies is not null && _previousSamples.TryGetValue(pid, out var previous))
            {
                var wallClockSeconds = (sampledAt - previous.SampledAt).TotalSeconds;
                var jiffiesDelta = cpuJiffies.Value - previous.Jiffies;

                if (wallClockSeconds > 0 && jiffiesDelta >= 0)
                {
                    // CPU% across all cores. Jiffies / (HZ * wall-seconds) * 100.
                    cpuPercent = Math.Max(0, jiffiesDelta / _jiffiesPerSecond / wallClockSeconds * 100.0);
                }
            }

            if (cpuJiffies is not null)
            {
                _previousSamples[pid] = new CpuSample(cpuJiffies.Value, sampledAt);
            }

            var handleCount = ReadFileDescriptorCount(pid);

            return new ProcessResourceSnapshot(cpuPercent, memoryMb.Value, handleCount, sampledAt);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _logger.LogDebug(exception, "Failed to sample resources for PID {Pid}", pid);
            _previousSamples.TryRemove(pid, out _);
            return null;
        }
    }

    public void Forget(int pid) => _previousSamples.TryRemove(pid, out _);

    private static double? ReadMemoryMb(int pid)
    {
        var statusPath = $"/proc/{pid.ToString(CultureInfo.InvariantCulture)}/status";

        if (!File.Exists(statusPath))
        {
            return null;
        }

        // /proc/<pid>/status is line-oriented "Key:\twhitespace-separated values".
        // VmRSS line shape: "VmRSS:\t   12345 kB"
        foreach (var line in File.ReadLines(statusPath))
        {
            if (!line.StartsWith("VmRSS:", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // ["VmRSS:", "<kb>", "kB"]
            return parts.Length >= 2 && long.TryParse(parts[1], CultureInfo.InvariantCulture, out var kilobytes)
                ? kilobytes / 1024.0
                : null;
        }

        return null;
    }

    private static long? ReadCpuJiffies(int pid)
    {
        var statPath = $"/proc/{pid.ToString(CultureInfo.InvariantCulture)}/stat";

        if (!File.Exists(statPath))
        {
            return null;
        }

        var content = File.ReadAllText(statPath);

        // /proc/<pid>/stat layout: "<pid> (<comm>) <state> <ppid> ..."
        // The comm field is in parentheses and may contain spaces, so we split after
        // the LAST closing paren to avoid being fooled by an executable named "foo bar".
        var lastParen = content.LastIndexOf(')');

        if (lastParen < 0 || lastParen + 2 > content.Length)
        {
            return null;
        }

        // After "<pid> (<comm>) ", field 3 (state) starts. Tokens are 1-indexed
        // per proc(5); we want fields 14 (utime) and 15 (stime), which are tokens
        // 12 and 13 of the post-paren remainder.
        var remainder = content[(lastParen + 2)..];

        var tokens = remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // tokens[0] = state, tokens[1] = ppid, ..., tokens[11] = utime, tokens[12] = stime
        return tokens.Length >= 13
            && long.TryParse(tokens[11], CultureInfo.InvariantCulture, out var utime)
            && long.TryParse(tokens[12], CultureInfo.InvariantCulture, out var stime)
            ? utime + stime
            : null;
    }

    private static int? ReadFileDescriptorCount(int pid)
    {
        var fdDir = $"/proc/{pid.ToString(CultureInfo.InvariantCulture)}/fd";

        if (!Directory.Exists(fdDir))
        {
            return null;
        }

        try
        {
            // EnumerateFileSystemEntries is cheaper than GetFileSystemEntries (no array
            // materialization) and we only want the count.
            var count = 0;

            foreach (var _ in Directory.EnumerateFileSystemEntries(fdDir))
            {
                count++;
            }

            return count;
        }
        catch (UnauthorizedAccessException)
        {
            // /proc/<pid>/fd requires the same UID as the process owner. If Collabhost
            // is running as a less-privileged user than the managed process, this read
            // fails -- not a fatal error for the sample as a whole.
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            // Process exited between the Directory.Exists check and the enumeration.
            return null;
        }
    }

    private record struct CpuSample(long Jiffies, DateTime SampledAt);
}
