namespace Collabhost.Api.Shared;

public static partial class LogLevelParser
{
    // Normalized 3-letter level codes
    private const string _info = "INF";
    private const string _warning = "WRN";
    private const string _error = "ERR";
    private const string _debug = "DBG";
    private const string _fatal = "FTL";

    // .NET Microsoft.Extensions.Logging prefixes (e.g., "info: Microsoft.Hosting.Lifetime[0]")
    [GeneratedRegex(@"^(?<level>info|warn|fail|crit|dbug|trce):", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking)]
    private static partial Regex MicrosoftLoggingPattern { get; }

    // Serilog bracket format (e.g., "[INF] Starting application" or "[10:30:00 INF] Starting")
    [GeneratedRegex(@"\[(?:\S+\s+)?(?<level>INF|WRN|ERR|DBG|FTL|VRB)\]", RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking)]
    private static partial Regex SerilogPattern { get; }

    // Generic/Node/Python level keywords at line start or after timestamp-like prefix
    // Matches: "INFO ...", "2024-01-15 10:30:00 ERROR ...", "WARNING: ...", "DEBUG ..."
    [GeneratedRegex(@"(?:^|\d{2}:\d{2}(?::\d{2})?\s+|\]\s*)(?<level>INFO|WARN|WARNING|ERROR|DEBUG|CRITICAL|FATAL)\b", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking)]
    private static partial Regex GenericPattern { get; }

    public static string? ParseLevel(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        // 1. .NET Microsoft.Extensions.Logging
        var microsoftMatch = MicrosoftLoggingPattern.Match(line);

        if (microsoftMatch.Success)
        {
            return NormalizeMicrosoftLevel(microsoftMatch.Groups["level"].Value);
        }

        // 2. Serilog bracket format
        var serilogMatch = SerilogPattern.Match(line);

        if (serilogMatch.Success)
        {
            return NormalizeSerilogLevel(serilogMatch.Groups["level"].Value);
        }

        // 3. Generic (Node, Python, plain text)
        var genericMatch = GenericPattern.Match(line);

        return genericMatch.Success ? NormalizeGenericLevel(genericMatch.Groups["level"].Value) : null;
    }

    private static string? NormalizeMicrosoftLevel(string level) =>
        level.ToUpperInvariant() switch
        {
            "INFO" => _info,
            "WARN" => _warning,
            "FAIL" => _error,
            "CRIT" => _fatal,
            "DBUG" => _debug,
            "TRCE" => _debug,
            _ => null
        };

    private static string? NormalizeSerilogLevel(string level) =>
        level.ToUpperInvariant() switch
        {
            "INF" => _info,
            "WRN" => _warning,
            "ERR" => _error,
            "DBG" => _debug,
            "FTL" => _fatal,
            "VRB" => _debug,
            _ => null
        };

    private static string? NormalizeGenericLevel(string level) =>
        level.ToUpperInvariant() switch
        {
            "INFO" => _info,
            "WARN" => _warning,
            "WARNING" => _warning,
            "ERROR" => _error,
            "DEBUG" => _debug,
            "CRITICAL" => _fatal,
            "FATAL" => _fatal,
            _ => null
        };
}
