using System.Globalization;
using System.Text.Json.Nodes;

namespace Collabhost.Api.Services.Proxy;

public sealed class ProxyConfigGenerator(ProxySettings settings)
{
    private readonly ProxySettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    public JsonObject Generate(IReadOnlyList<AppRouteInfo> apps)
    {
        var routableApps = apps
            .Where(a => AppTypeBehavior.IsRoutable(a.AppTypeId))
            .ToList();

        var subjects = BuildSubjectList(routableApps);
        var routes = BuildRoutes(routableApps);

        return new JsonObject
        {
            ["admin"] = new JsonObject
            {
                ["listen"] = "localhost:2019"
            },
            ["apps"] = new JsonObject
            {
                ["pki"] = BuildPkiConfig(),
                ["tls"] = BuildTlsConfig(subjects),
                ["http"] = BuildHttpConfig(routes)
            }
        };
    }

    private JsonArray BuildSubjectList(IReadOnlyList<AppRouteInfo> routableApps)
    {
        var subjects = new JsonArray
        {
            $"collabhost.{_settings.BaseDomain}"
        };

        foreach (var app in routableApps)
        {
            subjects.Add($"{app.Slug}.{_settings.BaseDomain}");
        }

        return subjects;
    }

    private JsonArray BuildRoutes(IReadOnlyList<AppRouteInfo> routableApps)
    {
        var routes = new JsonArray
        {
            BuildSelfRoute()
        };

        foreach (var app in routableApps)
        {
            var proxyMode = AppTypeBehavior.ProxyMode(app.AppTypeId);
            var route = proxyMode switch
            {
                "reverse_proxy" => BuildReverseProxyRoute(app),
                "file_server" => BuildFileServerRoute(app),
                _ => null
            };

            if (route is not null)
            {
                routes.Add(route);
            }
        }

        return routes;
    }

    private JsonObject BuildSelfRoute() =>
        new()
        {
            ["@id"] = "route_collabhost",
            ["match"] = new JsonArray
            {
                new JsonObject
                {
                    ["host"] = new JsonArray { $"collabhost.{_settings.BaseDomain}" }
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
                            ["dial"] = $"localhost:{_settings.SelfPort.ToString(CultureInfo.InvariantCulture)}"
                        }
                    }
                }
            },
            ["terminal"] = true
        };

    private JsonObject BuildReverseProxyRoute(AppRouteInfo app) =>
        new()
        {
            ["@id"] = $"route_{app.Slug}",
            ["match"] = new JsonArray
            {
                new JsonObject
                {
                    ["host"] = new JsonArray { $"{app.Slug}.{_settings.BaseDomain}" }
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
                            ["dial"] = $"localhost:{app.Port?.ToString(CultureInfo.InvariantCulture)}"
                        }
                    }
                }
            },
            ["terminal"] = true
        };

    private JsonObject BuildFileServerRoute(AppRouteInfo app) =>
        new()
        {
            ["@id"] = $"route_{app.Slug}",
            ["match"] = new JsonArray
            {
                new JsonObject
                {
                    ["host"] = new JsonArray { $"{app.Slug}.{_settings.BaseDomain}" }
                }
            },
            ["handle"] = new JsonArray
            {
                new JsonObject
                {
                    ["handler"] = "subroute",
                    ["routes"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["handle"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["handler"] = "vars",
                                    ["root"] = app.InstallDirectory
                                }
                            }
                        },
                        new JsonObject
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
                        },
                        new JsonObject
                        {
                            ["handle"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["handler"] = "file_server"
                                }
                            }
                        }
                    }
                }
            },
            ["terminal"] = true
        };

    private JsonObject BuildPkiConfig() =>
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

    private JsonObject BuildTlsConfig(JsonArray subjects) =>
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
                                ["lifetime"] = _settings.CertLifetime
                            }
                        }
                    }
                }
            }
        };

    private JsonObject BuildHttpConfig(JsonArray routes) =>
        new()
        {
            ["servers"] = new JsonObject
            {
                ["srv0"] = new JsonObject
                {
                    ["listen"] = new JsonArray { _settings.ListenAddress },
                    ["routes"] = routes
                }
            }
        };
}
