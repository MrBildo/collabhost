namespace Collabhost.Api.Probes;

// Reads structural metadata for a static-site artifact directory: presence of an
// index.html, top-level html file count, total asset bytes (best-effort, capped),
// and a "has-nested-assets" flag for the wwwroot/ or assets/ pattern. Card #220.
public static class StaticSiteExtractor
{
    // Cap the recursive byte tally so a misregistered directory pointed at /home
    // doesn't walk a million files. 200MB feels right for the homelab static-site
    // shape -- a SPA bundle is typically a few MB; a media-heavy site is 50-100MB.
    private const long _maxBytesScanned = 200L * 1024 * 1024;

    private static readonly string[] _nestedAssetDirectoryNames =
    [
        "assets",
        "_next",
        "_astro",
        "_app",
        "static"
    ];

    public static RawStaticSiteData? Extract(string artifactDirectory)
    {
        if (!Directory.Exists(artifactDirectory))
        {
            return null;
        }

        var hasIndexHtml = File.Exists(Path.Combine(artifactDirectory, "index.html"));

        int htmlCount;

        try
        {
            htmlCount = Directory.GetFiles(artifactDirectory, "*.html").Length
                + Directory.GetFiles(artifactDirectory, "*.htm").Length;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        // If there's no entry-point shape at all, this isn't a static site.
        if (!hasIndexHtml && htmlCount == 0)
        {
            return null;
        }

        var hasNestedAssets = HasNestedAssetDirectory(artifactDirectory);
        var totalBytes = TallyAssetBytes(artifactDirectory);

        return new RawStaticSiteData(hasIndexHtml, htmlCount, totalBytes, hasNestedAssets);
    }

    private static bool HasNestedAssetDirectory(string artifactDirectory)
    {
        if (Directory.Exists(Path.Combine(artifactDirectory, "wwwroot")))
        {
            return true;
        }

        foreach (var name in _nestedAssetDirectoryNames)
        {
            if (Directory.Exists(Path.Combine(artifactDirectory, name)))
            {
                return true;
            }
        }

        return false;
    }

    private static long TallyAssetBytes(string artifactDirectory)
    {
        long total = 0;

        try
        {
            foreach (var path in Directory.EnumerateFiles(artifactDirectory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(path).Length;
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                if (total >= _maxBytesScanned)
                {
                    return _maxBytesScanned;
                }
            }
        }
        catch (IOException)
        {
            return total;
        }
        catch (UnauthorizedAccessException)
        {
            return total;
        }

        return total;
    }
}
