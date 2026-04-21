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
}
