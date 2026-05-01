using System.Globalization;

namespace Collabhost.Api.Platform;

public static class StartupStderr
{
    public static void Write
    (
        string summary,
        IReadOnlyList<(string Label, string Value)> details,
        IReadOnlyList<string> recoverySteps,
        int exitCode
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(recoverySteps);

        var writer = Console.Error;

        writer.WriteLine();
        writer.WriteLine($"Collabhost startup failed: {summary}");
        writer.WriteLine();

        if (details.Count > 0)
        {
            writer.WriteLine("Details:");

            foreach (var (label, value) in details)
            {
                writer.WriteLine($"  - {label}: {value}");
            }

            writer.WriteLine();
        }

        if (recoverySteps.Count > 0)
        {
            writer.WriteLine("Recovery:");

            for (var index = 0; index < recoverySteps.Count; index++)
            {
                var step = (index + 1).ToString(CultureInfo.InvariantCulture);
                writer.WriteLine($"  {step}. {recoverySteps[index]}");
            }

            writer.WriteLine();
        }

        writer.WriteLine($"Exit code: {exitCode.ToString(CultureInfo.InvariantCulture)}");
        writer.WriteLine();

        writer.Flush();
    }

    // Write the stderr block AND persist a crash log file to disk for post-mortem
    // diagnostics. Stdout/stderr are gone with the terminated process; the file is
    // what an operator finds afterward. Crash-log write is best-effort and never
    // throws -- the stderr block is still the primary signal.
    public static void WriteAndPersist
    (
        string summary,
        IReadOnlyList<(string Label, string Value)> details,
        IReadOnlyList<string> recoverySteps,
        int exitCode,
        string crashLogDirectory,
        int crashLogRetention,
        Exception? exception = null
    )
    {
        Write(summary, details, recoverySteps, exitCode);

        var content = CrashLog.BuildContent
        (
            timestampUtc: DateTimeOffset.UtcNow,
            summary: summary,
            details: details,
            recoverySteps: recoverySteps,
            exitCode: exitCode,
            exception: exception
        );

        var written = CrashLog.TryWrite
        (
            directory: crashLogDirectory,
            timestampUtc: DateTimeOffset.UtcNow,
            content: content,
            retention: crashLogRetention
        );

        if (written is not null)
        {
            Console.Error.WriteLine($"Crash log written to: {written}");
            Console.Error.WriteLine();
            Console.Error.Flush();
        }
    }
}
