using System.Globalization;

using Collabhost.Api.Capabilities;
using Collabhost.Api.Data;
using Collabhost.Api.Portal;
using Collabhost.Api.Proxy;

namespace Collabhost.Api.Installation;

// Enumerates Portal + per-app hostnames for the --update-hosts CLI. Runs BEFORE
// WebApplication.CreateBuilder (the CLI dispatch path returns from Program.cs early), so this
// type does its own minimal IConfiguration build and opens AppDbContext directly without going
// through DI. Card #345.
public static class HostsFileResolver
{
    // Loopback by design. Caddy listens on :80/:443 on all interfaces; every <slug>.<baseDomain>
    // resolves to the host running Collabhost. Reaching the dashboard from a different LAN
    // device is the operator's choice via the alternate paths in INSTALL.md §9.10.2.
    public const string LoopbackAddress = "127.0.0.1";

    public sealed record ResolvedHostnames
    (
        IReadOnlyList<HostsFileEntry> Entries,
        IReadOnlyList<string> CollisionWarnings
    );

    public static async Task<ResolvedHostnames> ResolveAsync
    (
        IConfiguration configuration,
        string dataDir,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDir);

        var portalSettings = PortalRegistration.ResolveSettings(configuration);
        var proxySettings = ProxyRegistration.ResolveSettings(configuration);

        var portalHostname = string.Format
        (
            CultureInfo.InvariantCulture,
            "{0}.{1}",
            portalSettings.Subdomain,
            proxySettings.BaseDomain
        );

        // Build EF context directly. The CLI runs before AddDataAccess() would normally register
        // the factory; recreating the same connection-string + WAL semantics here keeps the CLI
        // independent of the web host.
        var dbFile = Path.Combine(dataDir, "collabhost.db");

        if (!File.Exists(dbFile))
        {
            // No DB yet -- still emit the Portal hostname so a fresh install can pre-stage hosts
            // before first boot, even though no apps are registered yet.
            return new ResolvedHostnames
            (
                [new HostsFileEntry(LoopbackAddress, portalHostname)],
                []
            );
        }

        var connectionString = $"Data Source={dbFile}";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connectionString)
                .Options;

        await using var db = new AppDbContext(options);

        var apps = await db.Apps
            .AsNoTracking()
            .OrderBy(a => a.Slug)
                .ToListAsync(ct);

        // Pull overrides for every app in one round-trip; the resolver only needs the routing
        // section. Empty result is fine (apps with no overrides inherit defaults).
        var routingOverrides = await db.CapabilityOverrides
            .AsNoTracking()
            .Where(o => o.CapabilitySlug == "routing")
                .ToDictionaryAsync(o => o.AppId, o => o.ConfigurationJson, ct);

        // Resolve each app's effective DomainPattern by merging the type-level routing default
        // with any per-app override. Type-level defaults live on the AppType JSON files loaded by
        // TypeStore -- but TypeStore depends on file I/O and a TypeStore singleton the CLI hasn't
        // built. For the CLI, we use the schema default ("{slug}.{baseDomain}") for apps with no
        // override, and ResolveDomain on the override's pattern when present. This matches what
        // every built-in AppType ships today (none customize the routing default) and honors
        // operator overrides identically to the running API.
        const string defaultDomainPattern = "{slug}.{baseDomain}";

        var entries = new List<HostsFileEntry>
        {
            new(LoopbackAddress, portalHostname)
        };

        var hostnameCounts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [portalHostname] = 1
        };

        foreach (var app in apps)
        {
            var pattern = defaultDomainPattern;

            if (routingOverrides.TryGetValue(app.Id, out var overrideJson)
                && !string.IsNullOrWhiteSpace(overrideJson))
            {
                try
                {
                    var node = JsonNode.Parse(overrideJson);

                    if (node is JsonObject obj
                        && obj.TryGetPropertyValue("domainPattern", out var dpNode)
                        && dpNode is JsonValue dpValue
                        && dpValue.TryGetValue<string>(out var dp)
                        && !string.IsNullOrWhiteSpace(dp))
                    {
                        pattern = dp;
                    }
                }
                catch (JsonException)
                {
                    // Corrupted override JSON -- fall back to the type-level default. The
                    // surrounding API surface treats this app as broken on its own (settings page
                    // would fail to deserialize), so the CLI's job is to at least produce a
                    // working host entry from the default rather than refusing to run.
                }
            }

            var hostname = CapabilityResolver.ResolveDomain(pattern, app.Slug, proxySettings.BaseDomain);

            entries.Add(new HostsFileEntry(LoopbackAddress, hostname));

            hostnameCounts[hostname] = hostnameCounts.GetValueOrDefault(hostname) + 1;
        }

        // Sort alphabetically by hostname so the emitted block is deterministic and re-runs
        // produce byte-identical output (the no-change short-circuit depends on this).
        entries.Sort((a, b) => string.Compare(a.Hostname, b.Hostname, StringComparison.Ordinal));

        var warnings = new List<string>();

        foreach (var (hostname, count) in hostnameCounts)
        {
            if (count > 1)
            {
                warnings.Add
                (
                    string.Format
                    (
                        CultureInfo.InvariantCulture,
                        "Hostname '{0}' is declared by {1} apps. The hosts file accepts duplicates; "
                        + "the OS resolver picks the first match. Consider customizing one app's "
                        + "RoutingConfiguration.DomainPattern to disambiguate.",
                        hostname,
                        count
                    )
                );
            }
        }

        return new ResolvedHostnames(entries, warnings);
    }
}
