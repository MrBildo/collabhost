namespace Collabhost.Api.Probes;

public static class DotnetExtractor
{
    public static RawDotnetData? Extract(string artifactDirectory)
    {
        if (!Directory.Exists(artifactDirectory))
        {
            return null;
        }

        var runtimeConfigFile = FindRuntimeConfig(artifactDirectory);

        if (runtimeConfigFile is null)
        {
            return null;
        }

        var runtimeConfig = ParseRuntimeConfig(runtimeConfigFile);
        var depsJson = ParseDepsJson(artifactDirectory);

        return new RawDotnetData(runtimeConfig, depsJson);
    }

    private static string? FindRuntimeConfig(string directory)
    {
        try
        {
            var files = Directory.GetFiles(directory, "*.runtimeconfig.json");

            return files.Length > 0 ? files[0] : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static RawRuntimeConfig? ParseRuntimeConfig(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("runtimeOptions", out var runtimeOptions))
            {
                return null;
            }

            var tfm = runtimeOptions.TryGetProperty("tfm", out var tfmElement)
                ? tfmElement.GetString()
                : null;

            var frameworks = ParseFrameworkArray(runtimeOptions, "frameworks");
            var includedFrameworks = ParseFrameworkArray(runtimeOptions, "includedFrameworks");

            // Legacy single-framework format (pre-.NET 3.0)
            if (frameworks.Count == 0 && runtimeOptions.TryGetProperty("framework", out var singleFramework))
            {
                var name = singleFramework.TryGetProperty("name", out var n) ? n.GetString() : null;
                var version = singleFramework.TryGetProperty("version", out var v) ? v.GetString() : null;

                if (name is not null && version is not null)
                {
                    frameworks = [new RawFrameworkReference(name, version)];
                }
            }

            var configProperties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

            if (runtimeOptions.TryGetProperty("configProperties", out var configProps)
                && configProps.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in configProps.EnumerateObject())
                {
                    configProperties[property.Name] = property.Value.Clone();
                }
            }

            return new RawRuntimeConfig(tfm, frameworks, includedFrameworks, configProperties);
        }
        catch
        {
            return null;
        }
    }

    private static List<RawFrameworkReference> ParseFrameworkArray
    (
        JsonElement parent,
        string propertyName
    )
    {
        var result = new List<RawFrameworkReference>();

        if (!parent.TryGetProperty(propertyName, out var array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in array.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
            var version = item.TryGetProperty("version", out var v) ? v.GetString() : null;

            if (name is not null && version is not null)
            {
                result.Add(new RawFrameworkReference(name, version));
            }
        }

        return result;
    }

    private static RawDepsJson? ParseDepsJson(string directory)
    {
        try
        {
            var files = Directory.GetFiles(directory, "*.deps.json");

            if (files.Length == 0)
            {
                return null;
            }

            var json = File.ReadAllText(files[0]);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var runtimeTarget = root.TryGetProperty("runtimeTarget", out var rt)
                && rt.TryGetProperty("name", out var rtName)
                    ? rtName.GetString()
                    : null;

            var libraries = new Dictionary<string, RawDepsLibrary>(StringComparer.Ordinal);

            if (root.TryGetProperty("libraries", out var libs)
                && libs.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in libs.EnumerateObject())
                {
                    var type = property.Value.TryGetProperty("type", out var t)
                        ? t.GetString() ?? "unknown"
                        : "unknown";

                    var version = ExtractVersionFromKey(property.Name);

                    libraries[property.Name] = new RawDepsLibrary(type, version);
                }
            }

            return new RawDepsJson(runtimeTarget, libraries);
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractVersionFromKey(string libraryKey)
    {
        // deps.json library keys are in the format "PackageName/Version"
        var slashIndex = libraryKey.LastIndexOf('/');

        return slashIndex >= 0 && slashIndex < libraryKey.Length - 1
            ? libraryKey[(slashIndex + 1)..]
            : null;
    }
}
