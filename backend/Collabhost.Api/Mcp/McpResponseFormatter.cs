using System.Globalization;

using ModelContextProtocol.Protocol;

namespace Collabhost.Api.Mcp;

public static class McpResponseFormatter
{
    private const int _maxTokenBudget = 8192;
    private const int _maxLineLength = 2048;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static CallToolResult AppNotFound(string slug) => new()
    {
        Content =
        [
            new TextContentBlock
            {
                Text = $"App '{slug}' not found. Use list_apps to see available apps and their slugs."
            }
        ],
        IsError = true
    };

    public static CallToolResult AppTypeNotFound(string slug) => new()
    {
        Content =
        [
            new TextContentBlock
            {
                Text = $"App type '{slug}' not found. Use list_app_types to see available types: "
                    + "dotnet-app, nodejs-app, static-site, executable, system-service."
            }
        ],
        IsError = true
    };

    public static CallToolResult InvalidStatusTransition
    (
        string slug,
        string currentStatus,
        string operation
    ) => new()
    {
        Content =
        [
            new TextContentBlock
            {
                Text = $"Cannot {operation} app '{slug}': it is already {currentStatus} "
                    + $"(status: {currentStatus}). Use get_app to check current status."
            }
        ],
        IsError = true
    };

    public static CallToolResult InvalidParameters(string message) => new()
    {
        Content = [new TextContentBlock { Text = message }],
        IsError = true
    };

    public static (string Content, string Summary) ApplyTokenBudget
    (
        IReadOnlyList<string> lines,
        int requestedLimit
    )
    {
        var effectiveLimit = Math.Min(requestedLimit, lines.Count);
        var tokenCount = 0;
        var includedCount = 0;

        for (var i = 0; i < effectiveLimit; i++)
        {
            var line = lines[i];

            if (line.Length > _maxLineLength)
            {
                line = line[.._maxLineLength] + "... (truncated)";
            }

            var lineTokens = EstimateTokens(line);

            if (tokenCount + lineTokens > _maxTokenBudget && includedCount > 0)
            {
                break;
            }

            tokenCount += lineTokens;
            includedCount++;
        }

        var included = lines
            .Take(includedCount)
                .Select(l => l.Length > _maxLineLength ? l[.._maxLineLength] + "... (truncated)" : l)
                    .ToList();

        var content = string.Join('\n', included);

        var summary = string.Create
        (
            CultureInfo.InvariantCulture,
            $"Returned {includedCount} of {lines.Count} log entries."
        );

        if (includedCount < lines.Count)
        {
            summary += string.Create
            (
                CultureInfo.InvariantCulture,
                $" Use offset={includedCount} to continue."
            );
        }

        return (content, summary);
    }

    public static string ToJson<T>(T value) =>
        JsonSerializer.Serialize(value, JsonOptions);

    public static CallToolResult Success(string text) => new()
    {
        Content = [new TextContentBlock { Text = text }]
    };

    private static int EstimateTokens(string text) =>
        (text.Length + 3) / 4;
}
