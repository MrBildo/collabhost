namespace Collabhost.Api.Probes;

public static class TypeScriptExtractor
{
    public static RawTypeScriptData? Extract(RawPackageJson? packageJson, string? projectRoot, string artifactDirectory)
    {
        var searchDirectory = ResolveSearchDirectory(projectRoot, artifactDirectory);

        if (searchDirectory is null)
        {
            return null;
        }

        var tsconfigPath = Path.Combine(searchDirectory, "tsconfig.json");

        if (!File.Exists(tsconfigPath))
        {
            return null;
        }

        var version = packageJson?.DevDependencies
            .GetValueOrDefault("typescript");

        var tsConfig = ParseTsConfig(tsconfigPath);

        return new RawTypeScriptData(version, tsConfig);
    }

    private static string? ResolveSearchDirectory(string? projectRoot, string artifactDirectory)
    {
        // Project root takes priority when set and exists
        if (!string.IsNullOrWhiteSpace(projectRoot) && Directory.Exists(projectRoot))
        {
            return projectRoot;
        }

        if (!Directory.Exists(artifactDirectory))
        {
            return null;
        }

        // Check the artifact directory first
        if (File.Exists(Path.Combine(artifactDirectory, "tsconfig.json")))
        {
            return artifactDirectory;
        }

        // Fallback: check the parent directory (one level up only).
        // This handles static sites where the artifact dir is a build output (e.g., dist/)
        // and the tsconfig.json lives in the project root above it.
        var parent = Directory.GetParent(artifactDirectory)?.FullName;

        return parent is not null && File.Exists(Path.Combine(parent, "tsconfig.json"))
            ? parent
            : artifactDirectory;
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
