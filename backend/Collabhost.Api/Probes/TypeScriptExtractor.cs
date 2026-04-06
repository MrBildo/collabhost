namespace Collabhost.Api.Probes;

public static class TypeScriptExtractor
{
    public static RawTypeScriptData? Extract(RawPackageJson? packageJson, string? projectRoot, string artifactDirectory)
    {
        // Reuse NodeExtractor's fallback search -- try projectRoot first, then artifactDirectory
        var tsconfigPath = NodeExtractor.FindFile("tsconfig.json", projectRoot, artifactDirectory);

        if (tsconfigPath is null)
        {
            return null;
        }

        var version = packageJson?.DevDependencies
            .GetValueOrDefault("typescript");

        var tsConfig = ParseTsConfig(tsconfigPath);

        return new RawTypeScriptData(version, tsConfig);
    }

    private static RawTsConfig? ParseTsConfig(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);

            // tsconfig.json may contain comments and trailing commas.
            // Use a lenient parse option.
            var options = new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            var doc = JsonDocument.Parse(json, options);
            var root = doc.RootElement;

            bool? strict = null;
            string? target = null;
            string? module = null;

            if (root.TryGetProperty("compilerOptions", out var compilerOptions))
            {
                strict = compilerOptions.TryGetProperty("strict", out var s)
                    && s.ValueKind == JsonValueKind.True;

                target = compilerOptions.TryGetProperty("target", out var t)
                    ? t.GetString()
                    : null;

                module = compilerOptions.TryGetProperty("module", out var m)
                    ? m.GetString()
                    : null;
            }

            return new RawTsConfig(strict, target, module);
        }
        catch
        {
            return null;
        }
    }
}
