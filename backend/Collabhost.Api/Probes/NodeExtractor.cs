namespace Collabhost.Api.Probes;

public static class NodeExtractor
{
    public static RawNodeData? Extract(string? projectRoot, string artifactDirectory)
    {
        var searchDirectory = ResolveSearchDirectory(projectRoot, artifactDirectory);

        if (searchDirectory is null)
        {
            return null;
        }

        var packageJsonPath = Path.Combine(searchDirectory, "package.json");

        if (!File.Exists(packageJsonPath))
        {
            return null;
        }

        var packageJson = ParsePackageJson(packageJsonPath);

        if (packageJson is null)
        {
            return null;
        }

        var lockfile = DetectLockfile(searchDirectory);

        return new RawNodeData(packageJson, lockfile);
    }

    private static string? ResolveSearchDirectory(string? projectRoot, string artifactDirectory) =>
        // Project root takes priority when set and exists
        !string.IsNullOrWhiteSpace(projectRoot) && Directory.Exists(projectRoot)
            ? projectRoot
            : Directory.Exists(artifactDirectory) ? artifactDirectory : null;

    private static RawPackageJson? ParsePackageJson(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            var version = root.TryGetProperty("version", out var v) ? v.GetString() : null;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;

            string? engineNode = null;

            if (root.TryGetProperty("engines", out var engines)
                && engines.TryGetProperty("node", out var nodeEngine))
            {
                engineNode = nodeEngine.GetString();
            }

            string? packageManager = null;

            if (root.TryGetProperty("packageManager", out var pm))
            {
                packageManager = pm.GetString();
            }

            var dependencies = ParseDependencyMap(root, "dependencies");
            var devDependencies = ParseDependencyMap(root, "devDependencies");

            return new RawPackageJson
            (
                name,
                version,
                type,
                engineNode,
                packageManager,
                dependencies,
                devDependencies
            );
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, string> ParseDependencyMap(JsonElement root, string propertyName)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!root.TryGetProperty(propertyName, out var deps)
            || deps.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var property in deps.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                result[property.Name] = property.Value.GetString()!;
            }
        }

        return result;
    }

    private static string? DetectLockfile(string directory) =>
        File.Exists(Path.Combine(directory, "pnpm-lock.yaml"))
            ? "pnpm"
            : File.Exists(Path.Combine(directory, "yarn.lock"))
                ? "yarn"
                : File.Exists(Path.Combine(directory, "bun.lockb")) || File.Exists(Path.Combine(directory, "bun.lock"))
                    ? "bun"
                    : File.Exists(Path.Combine(directory, "package-lock.json")) ? "npm" : null;
}
