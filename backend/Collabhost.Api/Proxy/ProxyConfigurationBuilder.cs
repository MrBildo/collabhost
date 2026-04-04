using System.Globalization;

using Collabhost.Api.Registry;

namespace Collabhost.Api.Proxy;

public static class ProxyConfigurationBuilder
{
    public static JsonObject Build
    (
        IReadOnlyList<RouteEntry> routes,
        ProxySettings settings
    )
    {
        var subjects = BuildSubjectList(routes, settings);
        var caddyRoutes = BuildRoutes(routes, settings);

        return new JsonObject
        {
            ["admin"] = new JsonObject
            {
                ["listen"] = "localhost:2019"
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
        ProxySettings settings
    )
    {
        var subjects = new JsonArray
        {
            $"collabhost.{settings.BaseDomain}"
        };

        foreach (var route in routes)
        {
            subjects.Add($"{route.Slug}.{settings.BaseDomain}");
        }

        return subjects;
    }

    private static JsonArray BuildRoutes
    (
        IReadOnlyList<RouteEntry> routes,
        ProxySettings settings
    )
    {
        var caddyRoutes = new JsonArray
        {
            BuildSelfRoute(settings)
        };

        foreach (var route in routes)
        {
            var caddyRoute = route switch
            {
                { Enabled: false } => BuildDisabledRoute(route, settings),
                { ServeMode: ServeMode.ReverseProxy } => BuildReverseProxyRoute(route, settings),
                { ServeMode: ServeMode.FileServer } => BuildFileServerRoute(route, settings),
                _ => null
            };

            if (caddyRoute is not null)
            {
                caddyRoutes.Add(caddyRoute);
            }
        }

        return caddyRoutes;
    }

    private static JsonObject BuildSelfRoute(ProxySettings settings) =>
        new()
        {
            ["@id"] = "route_collabhost",
            ["match"] = new JsonArray
            {
                new JsonObject
                {
                    ["host"] = new JsonArray { $"collabhost.{settings.BaseDomain}" }
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
                            ["dial"] = $"localhost:{settings.SelfPort.ToString(CultureInfo.InvariantCulture)}"
                        }
                    }
                }
            },
            ["terminal"] = true
        };

    private static JsonObject BuildDisabledRoute(RouteEntry route, ProxySettings settings) =>
        new()
        {
            ["@id"] = $"route_{route.Slug}",
            ["match"] = new JsonArray
            {
                new JsonObject
                {
                    ["host"] = new JsonArray { $"{route.Slug}.{settings.BaseDomain}" }
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

    private static JsonObject BuildReverseProxyRoute(RouteEntry route, ProxySettings settings)
    {
        var dialPort = route.Port?.ToString(CultureInfo.InvariantCulture) ?? "0";

        return new JsonObject
        {
            ["@id"] = $"route_{route.Slug}",
            ["match"] = new JsonArray
            {
                new JsonObject
                {
                    ["host"] = new JsonArray { $"{route.Slug}.{settings.BaseDomain}" }
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

    private static JsonObject BuildFileServerRoute(RouteEntry route, ProxySettings settings)
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
                    ["host"] = new JsonArray { $"{route.Slug}.{settings.BaseDomain}" }
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
    ServeMode ServeMode,
    int? Port,
    bool SpaFallback,
    string? ArtifactDirectory,
    bool Enabled
);
