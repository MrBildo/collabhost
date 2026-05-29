using System.Globalization;

using Collabhost.Api.Capabilities.Configurations;
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
                    },
                    // Trust nothing inbound -- see BuildReverseProxyRoute for the
                    // full rationale. Same defended-edge posture applies to the
                    // Portal route. Card #360.
                    ["trusted_proxies"] = new JsonArray()
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
        // External-target routes (Card #348) carry a pre-resolved dial
        // address and the producer (ProxyManager.LoadRoutableAppsAsync) is
        // the single source of truth for which shape applies. Supervised-
        // process routes synthesize localhost:{Port} as before.
        var dial = route.ExternalDial
            ?? string.Format
            (
                CultureInfo.InvariantCulture,
                "localhost:{0}",
                route.Port?.ToString(CultureInfo.InvariantCulture) ?? "0"
            );

        // Emit `trusted_proxies: []` on every reverse_proxy handler. Caddy 2.10+
        // requires explicit per-handler trusted_proxies configuration for
        // upstream X-Forwarded-For propagation in scenarios where the immediate
        // peer is not the real client. With an empty list no source is trusted:
        // Caddy treats the immediate peer IP as the real client and sets
        // X-Forwarded-For from it on every outbound request to the upstream,
        // ignoring any prior X-Forwarded-* values. This is the defended edge-
        // proxy posture -- Collabhost IS the edge today, no upstream proxy
        // should be trusted by default. Operators running Collabhost behind
        // another proxy (Cloudflare Tunnel, on-prem LB, Tailscale Funnel) will
        // need a future operator-configurable `TrustedProxyRanges` setting --
        // separate scope, follow-up. Card #360.
        var reverseProxyHandler = new JsonObject
        {
            ["handler"] = "reverse_proxy",
            ["upstreams"] = new JsonArray
            {
                new JsonObject
                {
                    ["dial"] = dial
                }
            },
            ["trusted_proxies"] = new JsonArray()
        };

        // HTTPS upstream needs Caddy's transport block so the proxy speaks
        // TLS to the backend. An empty `tls` object means "use Caddy's
        // default TLS verify against the system trust store" -- operators
        // fronting a self-signed upstream can later opt into
        // `transport.tls.insecure_skip_verify` via the v2 transport-config
        // capability arc; for v1 of external-route, default-verify is the
        // safe posture. Card #348, D2.
        if (string.Equals(route.ExternalScheme, "https", StringComparison.Ordinal))
        {
            reverseProxyHandler["transport"] = new JsonObject
            {
                ["protocol"] = "http",
                ["tls"] = new JsonObject()
            };
        }

        // Conditional subroute-wrap on non-empty security-headers emission
        // (precondition #6). When no security-header handler is emitted the
        // route stays flat -- byte-identical to the pre-#309 reverse-proxy
        // shape so an operator who suppresses all security headers (or any
        // future state without a security-headers binding) gets back the
        // un-wrapped emission. Card #309.
        var securityHeaderHandler = BuildSecurityHeaderHandler(route.SecurityHeaders);

        var handle = new JsonArray();

        if (securityHeaderHandler is not null)
        {
            var subrouteHandlers = new JsonArray
            {
                securityHeaderHandler,
                new JsonObject
                {
                    ["handle"] = new JsonArray { reverseProxyHandler }
                }
            };

            handle.Add
            (
                new JsonObject
                {
                    ["handler"] = "subroute",
                    ["routes"] = subrouteHandlers
                }
            );
        }
        else
        {
            handle.Add(reverseProxyHandler);
        }

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
            ["handle"] = handle,
            ["terminal"] = true
        };
    }

    private static JsonObject BuildFileServerRoute(RouteEntry route)
    {
        var subrouteHandlers = new JsonArray
        {
            BuildFileServerRootHandler(route)
        };

        // Runtime-config-file overlay (#369). A terminal, path-scoped subroute
        // that serves the config path from the writable data dir, inserted
        // immediately after the blanket artifact root and ahead of every other
        // handler. It is self-contained: it sets ITS OWN root (writable), its
        // own no-cache header, and runs file_server as the responder -- so the
        // request for the config path is served-and-done inside this branch.
        //
        // CRITICAL: this branch must not let vars.root bleed into the SPA-
        // fallback try_files. The path match scopes the nested subroute to the
        // config path only; for every other request the match fails, the nested
        // subroute never executes, and vars.root stays the artifact root set
        // above -- so BuildSpaFallbackHandler still stats the artifact tree to
        // decide the /index.html rewrite. Asserted in the proxy-emission tests.
        var overlayHandler = BuildRuntimeConfigOverlayHandler(route);

        if (overlayHandler is not null)
        {
            subrouteHandlers.Add(overlayHandler);
        }

        // Blanket security-headers handler (Card #309) inserted BEFORE the
        // path-matched response-header handlers (Card #308). Caddy's
        // `response.set` is last-write-wins on the response-commit deferred-
        // mutation chain, so the path-scoped handler -- emitted later in the
        // chain -- wins on overlapping header names. Same shape as CSS
        // specificity: the more-specific path rule beats the blanket rule
        // when they collide on the same key. Precondition #7.
        var securityHeaderHandler = BuildSecurityHeaderHandler(route.SecurityHeaders);

        if (securityHeaderHandler is not null)
        {
            subrouteHandlers.Add(securityHeaderHandler);
        }

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

    // Terminal, path-scoped overlay that serves the runtime-config file from the
    // app's writable data dir (#369). Returns null when the capability is not
    // declared (both fields null) -- the migration-safe default that keeps the
    // file-server shape byte-identical to before this field existed.
    //
    // Shape: an outer route matched on the config path whose handle is a nested
    // subroute carrying, in order, a vars handler that sets the writable root, a
    // headers handler that sets no-cache, and a file_server responder. Wrapping
    // the root switch plus responder inside a path-matched subroute is what keeps
    // the root from bleeding to the rest of the chain -- the nested subroute only
    // runs for the matched config path; every other request skips it and keeps
    // the artifact root. file_server is a responder, so the matched request is
    // served-and-done, terminal in effect.
    //
    // The no-cache header rides INSIDE this branch (not the #308 path-header
    // handler) so the overlay is self-contained -- it does not depend on a
    // separate header handler firing first in the chain ordering (#369 Q1 / Remy
    // cross-critique).
    private static JsonObject? BuildRuntimeConfigOverlayHandler(RouteEntry route)
    {
        if (string.IsNullOrEmpty(route.RuntimeConfigFilePath)
            || string.IsNullOrEmpty(route.RuntimeConfigWritableRoot))
        {
            return null;
        }

        var innerSubrouteHandlers = new JsonArray
        {
            new JsonObject
            {
                ["handle"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["handler"] = "vars",
                        ["root"] = route.RuntimeConfigWritableRoot
                    }
                }
            },
            new JsonObject
            {
                ["handle"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["handler"] = "headers",
                        ["response"] = new JsonObject
                        {
                            ["set"] = new JsonObject
                            {
                                ["Cache-Control"] = new JsonArray { "no-cache" }
                            }
                        }
                    }
                }
            },
            new JsonObject
            {
                ["handle"] = new JsonArray
                {
                    new JsonObject { ["handler"] = "file_server" }
                }
            }
        };

        return new JsonObject
        {
            ["match"] = new JsonArray
            {
                new JsonObject
                {
                    ["path"] = new JsonArray { route.RuntimeConfigFilePath }
                }
            },
            ["handle"] = new JsonArray
            {
                new JsonObject
                {
                    ["handler"] = "subroute",
                    ["routes"] = innerSubrouteHandlers
                }
            }
        };
    }

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

    // Builds the single blanket (un-matched) `headers` handler for the route's
    // security-headers spec. Returns null when emission should be skipped --
    // the empty-no-op invariant: !EnableHsts AND empty Headers map (after
    // dropping empty-string-valued entries) -- so the caller can also skip
    // the subroute-wrap on reverse-proxy routes (precondition #1 + #6).
    //
    // Emission semantics:
    //  - EnableHsts: true expands to a Strict-Transport-Security entry whose
    //    value is computed from HstsMaxAgeSeconds. max-age=0 is the legitimate
    //    rollback / un-pin signal (precondition #4).
    //  - Headers map entries are emitted verbatim; entries whose value is the
    //    empty string are DROPPED (operator-suppression mechanism, precondition
    //    #5). The operator who needs to suppress XCTO sets its value to "" in
    //    the Headers map; the entry survives MergeJson (it is an override over
    //    the type-default value) and the emitter skips it here.
    //  - Output ordering is ordinal by header name for snapshot stability.
    //
    // Card #309.
    private static JsonObject? BuildSecurityHeaderHandler(SecurityHeadersConfiguration? resolved)
    {
        if (resolved is null)
        {
            return null;
        }

        // Empty-no-op invariant at the builder, on the resolved spec
        // (precondition #1). Mirrors #336's RuntimeConfigFileWriter empty-
        // Values short-circuit -- ZERO emission when there is nothing to
        // emit. Critical for the reverse-proxy subroute-wrap conditional:
        // without this, every reverse-proxy route would gain a subroute even
        // when there is no header to set.
        if (!resolved.EnableHsts && (resolved.Headers is null || resolved.Headers.Count == 0))
        {
            return null;
        }

        var set = new SortedDictionary<string, string>(StringComparer.Ordinal);

        if (resolved.EnableHsts)
        {
            var maxAge = resolved.HstsMaxAgeSeconds < 0 ? 0 : resolved.HstsMaxAgeSeconds;

            set["Strict-Transport-Security"] = string.Format
            (
                CultureInfo.InvariantCulture,
                "max-age={0}",
                maxAge
            );
        }

        if (resolved.Headers is not null)
        {
            foreach (var (name, value) in resolved.Headers)
            {
                // Operator-suppression: an empty-string value is the documented
                // escape hatch for type-default rows (notably the XCTO seed).
                // MergeJson is one-level-deep and cannot delete a type-default
                // entry by clearing the override map; the suppression channel
                // is per-entry override-to-empty + drop-at-emission. Card #309
                // precondition #5 (Bill ruling).
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                set[name] = value;
            }
        }

        // If only convenience flags fired and the post-suppression header set
        // is empty (e.g. EnableHsts: false + Headers: {"X-Content-Type-Options": ""})
        // re-check the empty-no-op invariant on the materialized output.
        if (set.Count == 0)
        {
            return null;
        }

        var setObject = new JsonObject();

        foreach (var (name, value) in set)
        {
            setObject[name] = new JsonArray { value };
        }

        return new JsonObject
        {
            ["handle"] = new JsonArray
            {
                new JsonObject
                {
                    ["handler"] = "headers",
                    ["response"] = new JsonObject
                    {
                        ["set"] = setObject
                    }
                }
            }
        };
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
    IReadOnlyDictionary<string, string>? ResponseHeaders = null,
    // Resolved security-headers spec for this route. Null = capability not
    // bound on the app type (e.g. system-service) or app explicitly opted out
    // -- emission skipped, subroute-wrap on reverse-proxy stays flat. Applies
    // to both reverse-proxy and file-server routes; the emission helper at
    // the builder is the single source of truth for "should anything be
    // emitted." Card #309.
    SecurityHeadersConfiguration? SecurityHeaders = null,
    // Pre-resolved "host:port" dial address for routes whose upstream is NOT a
    // Collabhost-supervised process. When set, BuildReverseProxyRoute dials
    // this instead of synthesizing localhost:{Port}. Mutually exclusive in
    // semantics with Port -- Port carries the supervised-process port-injection
    // shape, ExternalDial carries the unmanaged-upstream shape. The producer
    // (ProxyManager.LoadRoutableAppsAsync) is the single source of truth for
    // which shape applies. The record carries both fields so existing call
    // sites stay clean. Card #348.
    string? ExternalDial = null,
    // Upstream scheme for external-target routes. "http" (default when
    // ExternalDial is set and unspecified) or "https". When "https", the
    // builder emits a Caddy transport block so the proxy speaks TLS to the
    // upstream. Ignored when ExternalDial is null. Card #348.
    string? ExternalScheme = null,
    // Runtime-config-file overlay (#369). Both null = capability not declared on
    // this app's type -> NO new emission, byte-identical to the pre-#369 file-
    // server shape (the migration-safe default, asserted in tests). When BOTH
    // are non-null, BuildFileServerRoute emits a terminal, path-scoped overlay
    // subroute that serves RuntimeConfigFilePath from RuntimeConfigWritableRoot
    // ahead of the artifact-rooted blanket file_server. The handler MUST be
    // terminal so its root switch never bleeds into the SPA-fallback try_files
    // (which stats vars.root to decide the /index.html rewrite). File-server
    // routes only.
    string? RuntimeConfigFilePath = null,
    string? RuntimeConfigWritableRoot = null
);
