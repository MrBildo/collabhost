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

        return new JsonObject
        {
            ["admin"] = new JsonObject
            {
                ["listen"] = adminListen
            },
            ["apps"] = new JsonObject
            {
                ["pki"] = BuildPkiConfiguration(),
                ["tls"] = BuildTlsConfiguration(subjects, settings),
                ["http"] = BuildHttpConfiguration(caddyRoutes, settings)
            }
        };
    }

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

    private static JsonObject BuildTlsConfiguration(JsonArray subjects, ProxySettings settings) =>
        new()
        {
            ["automation"] = new JsonObject
            {
                ["policies"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["subjects"] = subjects,
                        ["issuers"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["module"] = "internal",
                                ["ca"] = "local",
                                ["lifetime"] = settings.CertLifetime
                            }
                        }
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
                    ["listen"] = new JsonArray { settings.ListenAddress },
                    ["routes"] = routes
                }
            }
        };
}

public record RouteEntry
(
    string Slug,
    string Domain,
    ServeMode ServeMode,
    int? Port,
    bool SpaFallback,
    string? ArtifactDirectory,
    bool Enabled
);
