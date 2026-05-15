using System.Globalization;

using Collabhost.Api.Platform;
using Collabhost.Api.Portal;
using Collabhost.Api.Registry;

namespace Collabhost.Api.Proxy;

public static class ProxyConfigurationBuilder
{
    public static JsonObject Build
    (
        IReadOnlyList<RouteEntry> routes,
        ProxySettings settings,
        HostingSettings hostingSettings,
        PortalSettings portalSettings
    )
    {
        var subjects = BuildSubjectList(routes, settings, portalSettings);
        var caddyRoutes = BuildRoutes(routes, settings, hostingSettings, portalSettings);

        var adminListen = string.Format
        (
            CultureInfo.InvariantCulture,
            "localhost:{0}",
            settings.AdminPort
        );

        var apps = new JsonObject();

        // The internal-CA issuer relies on Caddy's local PKI block to advertise
        // the local authority. The ACME branch (Let's Encrypt) ignores any local
        // CA configuration -- omit the pki block entirely when DnsProvider is
        // set so the emitted config carries no dead weight.
        if (string.IsNullOrWhiteSpace(settings.DnsProvider))
        {
            apps["pki"] = BuildPkiConfiguration();
        }

        apps["tls"] = BuildTlsConfiguration(subjects, settings);
        apps["http"] = BuildHttpConfiguration(caddyRoutes, settings);

        var config = new JsonObject
        {
            ["admin"] = new JsonObject
            {
                ["listen"] = adminListen
            },
            ["apps"] = apps
        };

        // Operator-controlled Caddy storage path. Emitted only when set so the unset
        // case preserves Caddy's built-in default (XDG_DATA_HOME on Linux, %AppData%
        // \Caddy on Windows, $HOME/Library/Application Support/Caddy on macOS) bit-for
        // -bit. System-install operators with a dedicated service user use this lever
        // to pin CA / account / cert data at a known persistent path independent of
        // the service user's $HOME / XDG resolution. Card #230 phase 1.
        if (!string.IsNullOrWhiteSpace(settings.StoragePath))
        {
            config["storage"] = BuildStorageConfiguration(settings.StoragePath);
        }

        return config;
    }

    private static JsonObject BuildStorageConfiguration(string storagePath) =>
        new()
        {
            ["module"] = "file_system",
            ["root"] = storagePath
        };

    private static JsonArray BuildSubjectList
    (
        IReadOnlyList<RouteEntry> routes,
        ProxySettings settings,
        PortalSettings portalSettings
    )
    {
        var subjects = new JsonArray
        {
            $"{portalSettings.Subdomain}.{settings.BaseDomain}"
        };

        foreach (var route in routes)
        {
            subjects.Add(route.Domain);
        }

        return subjects;
    }

    private static JsonArray BuildRoutes
    (
        IReadOnlyList<RouteEntry> routes,
        ProxySettings settings,
        HostingSettings hostingSettings,
        PortalSettings portalSettings
    )
    {
        var caddyRoutes = new JsonArray
        {
            BuildSelfRoute(settings, hostingSettings, portalSettings)
        };

        foreach (var route in routes)
        {
            var caddyRoute = route switch
            {
                { Enabled: false } => BuildDisabledRoute(route),
                { ServeMode: ServeMode.ReverseProxy } => BuildReverseProxyRoute(route),
                { ServeMode: ServeMode.FileServer } => BuildFileServerRoute(route),
                _ => null
            };

            if (caddyRoute is not null)
            {
                caddyRoutes.Add(caddyRoute);
            }
        }

        return caddyRoutes;
    }

    private static JsonObject BuildSelfRoute
    (
        ProxySettings settings,
        HostingSettings hostingSettings,
        PortalSettings portalSettings
    ) =>
        new()
        {
            // The @id literal is an internal Caddy CRUD identifier, never operator-visible.
            // Renaming it is a Caddy-config-state migration concern; pre-production posture
            // says don't (CLAUDE.md Rule 3). The host below now respects Portal:Subdomain.
            ["@id"] = "route_collabhost",
            ["match"] = new JsonArray
            {
                new JsonObject
                {
                    ["host"] = new JsonArray { $"{portalSettings.Subdomain}.{settings.BaseDomain}" }
                }
            },
            ["handle"] = new JsonArray
            {
                new JsonObject
                {
                    ["handler"] = "reverse_proxy",
                    // Flush immediately -- required for SSE log streaming through the proxy
                    ["flush_interval"] = -1,
                    ["upstreams"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["dial"] = $"localhost:{hostingSettings.ListenPort.ToString(CultureInfo.InvariantCulture)}"
                        }
                    }
                }
            },
            ["terminal"] = true
        };

    private static JsonObject BuildDisabledRoute(RouteEntry route) =>
        new()
        {
            ["@id"] = $"route_{route.Slug}",
            ["match"] = new JsonArray
            {
                new JsonObject
                {
                    ["host"] = new JsonArray { route.Domain }
                }
            },
            ["handle"] = new JsonArray
            {
                new JsonObject
                {
                    ["handler"] = "static_response",
                    ["status_code"] = "503",
                    ["headers"] = new JsonObject
                    {
                        ["Content-Type"] = new JsonArray { "text/plain" }
                    },
                    ["body"] = "Service Unavailable"
                }
            },
            ["terminal"] = true
        };

    private static JsonObject BuildReverseProxyRoute(RouteEntry route)
    {
        var dialPort = route.Port?.ToString(CultureInfo.InvariantCulture) ?? "0";

        return new JsonObject
        {
            ["@id"] = $"route_{route.Slug}",
            ["match"] = new JsonArray
            {
                new JsonObject
                {
                    ["host"] = new JsonArray { route.Domain }
                }
            },
            ["handle"] = new JsonArray
            {
                new JsonObject
                {
                    ["handler"] = "reverse_proxy",
                    ["upstreams"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["dial"] = $"localhost:{dialPort}"
                        }
                    }
                }
            },
            ["terminal"] = true
        };
    }

    private static JsonObject BuildFileServerRoute(RouteEntry route)
    {
        var subrouteHandlers = new JsonArray
        {
            BuildFileServerRootHandler(route)
        };

        // Path-matched response-header handlers. Caddy's `headers` handler is a
        // deferred response-header mutation, non-terminal -- it must be
        // encountered in the route chain before the response is committed.
        // Insert after the `vars` root and before the SPA rewrite + file_server
        // so the header is registered while the request still falls through to
        // the responder. The `path` match scopes each rule to its declared
        // path; unmatched files keep Caddy's default behavior. Card #308.
        foreach (var headerHandler in BuildResponseHeaderHandlers(route.ResponseHeaders))
        {
            subrouteHandlers.Add(headerHandler);
        }

        if (route.SpaFallback)
        {
            subrouteHandlers.Add(BuildSpaFallbackHandler());
        }

        subrouteHandlers.Add
        (
            new JsonObject
            {
                ["handle"] = new JsonArray
                {
                    new JsonObject { ["handler"] = "file_server" }
                }
            }
        );

        return new JsonObject
        {
            ["@id"] = $"route_{route.Slug}",
            ["match"] = new JsonArray
            {
                new JsonObject
                {
                    ["host"] = new JsonArray { route.Domain }
                }
            },
            ["handle"] = new JsonArray
            {
                new JsonObject
                {
                    ["handler"] = "subroute",
                    ["routes"] = subrouteHandlers
                }
            },
            ["terminal"] = true
        };
    }

    private static JsonObject BuildFileServerRootHandler(RouteEntry route) =>
        new()
        {
            ["handle"] = new JsonArray
            {
                new JsonObject
                {
                    ["handler"] = "vars",
                    ["root"] = route.ArtifactDirectory ?? ""
                }
            }
        };

    // Builds one path-matched `headers` handler per declared path. The
    // RouteEntry carries flattened "<path>::<HeaderName>" keys; rules are
    // grouped by path so a path with several headers yields one handler with
    // several `response.set` entries (tighter config, fewer chain hops).
    // Ordering is deterministic (ordinal by path, then by header name) so the
    // emitted config is stable across syncs and asserts cleanly in tests.
    // Malformed keys are skipped defensively -- ValidateEdits is the
    // authoritative gate, but a seed or legacy override should never produce
    // an invalid Caddy config.
    private static List<JsonObject> BuildResponseHeaderHandlers
    (
        IReadOnlyDictionary<string, string>? responseHeaders
    )
    {
        var handlers = new List<JsonObject>();

        if (responseHeaders is null || responseHeaders.Count == 0)
        {
            return handlers;
        }

        var byPath = new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);

        foreach (var (compoundKey, value) in responseHeaders)
        {
            var separatorIndex = compoundKey.IndexOf("::", StringComparison.Ordinal);

            if (separatorIndex <= 0 || separatorIndex + 2 >= compoundKey.Length)
            {
                continue;
            }

            var path = compoundKey[..separatorIndex];
            var headerName = compoundKey[(separatorIndex + 2)..];

            if (!path.StartsWith('/'))
            {
                continue;
            }

            if (!byPath.TryGetValue(path, out var headers))
            {
                headers = new SortedDictionary<string, string>(StringComparer.Ordinal);
                byPath[path] = headers;
            }

            headers[headerName] = value;
        }

        foreach (var (path, headers) in byPath)
        {
            var set = new JsonObject();

            foreach (var (headerName, value) in headers)
            {
                set[headerName] = new JsonArray { value };
            }

            handlers.Add
            (
                new JsonObject
                {
                    ["match"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["path"] = new JsonArray { path }
                        }
                    },
                    ["handle"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["handler"] = "headers",
                            ["response"] = new JsonObject
                            {
                                ["set"] = set
                            }
                        }
                    }
                }
            );
        }

        return handlers;
    }

    private static JsonObject BuildSpaFallbackHandler() =>
        new()
        {
            ["match"] = new JsonArray
            {
                new JsonObject
                {
                    ["file"] = new JsonObject
                    {
                        ["try_files"] = new JsonArray
                        {
                            "{http.request.uri.path}",
                            "/index.html"
                        }
                    }
                }
            },
            ["handle"] = new JsonArray
            {
                new JsonObject
                {
                    ["handler"] = "rewrite",
                    ["uri"] = "{http.matchers.file.relative}"
                }
            }
        };

    private static JsonObject BuildPkiConfiguration() =>
        new()
        {
            ["certificate_authorities"] = new JsonObject
            {
                ["local"] = new JsonObject
                {
                    ["name"] = "Collabhost Local Authority",
                    ["install_trust"] = false
                }
            }
        };

    private static JsonObject BuildTlsConfiguration(JsonArray subjects, ProxySettings settings)
    {
        var issuer = string.IsNullOrWhiteSpace(settings.DnsProvider)
            ? BuildInternalIssuer(settings)
            : BuildAcmeIssuer(settings);

        return new JsonObject
        {
            ["automation"] = new JsonObject
            {
                ["policies"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["subjects"] = subjects,
                        ["issuers"] = new JsonArray { issuer }
                    }
                }
            }
        };
    }

    private static JsonObject BuildInternalIssuer(ProxySettings settings) =>
        new()
        {
            ["module"] = "internal",
            ["ca"] = "local",
            ["lifetime"] = settings.CertLifetime
        };

    // ACME issuer with DNS-01 challenge. The api_token uses Caddy's {env.NAME}
    // placeholder so the secret is resolved from Caddy's process env at issue
    // time -- the token never appears in the JSON config snapshot, log diff,
    // or DB row. ProxyEnvironmentProvider plumbs the env var into the Caddy
    // child's process env at spawn time.
    //
    // No "lifetime" field: Let's Encrypt sets 90 days and rejects custom
    // lifetimes; CertLifetime is internal-CA-only.
    private static JsonObject BuildAcmeIssuer(ProxySettings settings) =>
        new()
        {
            ["module"] = "acme",
            ["challenges"] = new JsonObject
            {
                ["dns"] = new JsonObject
                {
                    ["provider"] = new JsonObject
                    {
                        ["name"] = settings.DnsProvider,
                        ["api_token"] = $"{{env.{settings.DnsApiTokenEnvVar}}}"
                    }
                }
            }
        };

    private static JsonObject BuildHttpConfiguration(JsonArray routes, ProxySettings settings) =>
        new()
        {
            ["servers"] = new JsonObject
            {
                ["srv0"] = new JsonObject
                {
                    ["listen"] = BuildListenArray(settings.ListenAddress),
                    ["routes"] = routes
                }
            }
        };

    // Parses the comma-separated ListenAddress into a Caddy listen array. Default
    // ":80,:443" becomes ["":80"", ":443""]. Caddy automatically promotes the :443 entry
    // into an HTTP->HTTPS-on-:80 redirect using the auto-generated http_redirect server,
    // so the operator gets http:// typo polish for free with the default. Card #217.
    internal static JsonArray BuildListenArray(string listenAddress)
    {
        var result = new JsonArray();

        if (string.IsNullOrWhiteSpace(listenAddress))
        {
            return result;
        }

        var entries = listenAddress.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var entry in entries)
        {
            result.Add(entry);
        }

        return result;
    }

    // Derives whether the proxy's configured listen surface includes a TLS-capable
    // listener. Caddy auto-enables TLS for any non-:80 listen entry; the conservative
    // and accurate signal is "the listen list contains :443" (the canonical HTTPS port).
    // Other operator-chosen TLS ports would not match this; the surface this feeds
    // (App Detail Route card) is documenting the canonical posture, so :443 is the
    // right anchor. Card #263 item 1.3.
    public static bool HasTlsListener(string listenAddress)
    {
        if (string.IsNullOrWhiteSpace(listenAddress))
        {
            return false;
        }

        var entries = listenAddress.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var entry in entries)
        {
            if (entry.EndsWith(":443", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

public record RouteEntry
(
    string Slug,
    string Domain,
    ServeMode ServeMode,
    int? Port,
    bool SpaFallback,
    string? ArtifactDirectory,
    bool Enabled,
    // Flattened per-path response-header rules ("<path>::<HeaderName>" ->
    // value). Null or empty = no header handlers emitted (the file-server
    // subroute shape is identical to before this field existed -- the
    // migration-safe default). File-server routes only. Card #308.
    IReadOnlyDictionary<string, string>? ResponseHeaders = null
);
