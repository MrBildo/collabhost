using System.Text.Json.Nodes;

using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Platform;
using Collabhost.Api.Portal;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Proxy;

public class ProxyConfigurationBuilderTests
{
    private static readonly ProxySettings _defaultSettings = new()
    {
        BaseDomain = "collab.internal",
        BinaryPath = "caddy",
        ListenAddress = ":443",
        CertLifetime = "168h",
        AdminPort = 2019
    };

    private static readonly HostingSettings _defaultHosting = new()
    {
        ListenAddress = "localhost",
        ListenPort = 58400
    };

    private static readonly PortalSettings _defaultPortal = new()
    {
        Subdomain = "collabhost"
    };

    // Resolve the default-domain shape so RouteEntry call sites don't have to recompute.
    // Tests that target a custom domain pass it explicitly.
    private static string DefaultDomain(string slug, string baseDomain = "collab.internal") =>
        $"{slug}.{baseDomain}";

    [Fact]
    public void Build_EmptyRoutes_ReturnsSelfRouteOnly()
    {
        var config = ProxyConfigurationBuilder.Build([], _defaultSettings, _defaultHosting, _defaultPortal);

        var routes = GetRoutes(config);

        routes.ShouldNotBeNull();
        routes.Count.ShouldBe(1);
        routes[0]!["@id"]!.GetValue<string>().ShouldBe("route_collabhost");
    }

    [Fact]
    public void Build_SelfRoute_HasFlushInterval()
    {
        var config = ProxyConfigurationBuilder.Build([], _defaultSettings, _defaultHosting, _defaultPortal);

        var selfRoute = GetRoutes(config)![0]!;

        var handler = selfRoute["handle"]![0]!;

        handler["flush_interval"]!.GetValue<int>().ShouldBe(-1);
    }

    [Fact]
    public void Build_SelfRoute_HasCorrectHostAndDial()
    {
        var config = ProxyConfigurationBuilder.Build([], _defaultSettings, _defaultHosting, _defaultPortal);

        var selfRoute = GetRoutes(config)![0]!;

        var host = selfRoute["match"]![0]!["host"]![0]!.GetValue<string>();
        host.ShouldBe("collabhost.collab.internal");

        var dial = selfRoute["handle"]![0]!["upstreams"]![0]!["dial"]!.GetValue<string>();
        dial.ShouldBe("localhost:58400");
    }

    [Fact]
    public void Build_ReverseProxyRoute_HasCorrectStructure()
    {
        var routes = new List<RouteEntry>
        {
            new("my-app", DefaultDomain("my-app"), ServeMode.ReverseProxy, Port: 5000, SpaFallback: false, ArtifactDirectory: null, Enabled: true)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var caddyRoutes = GetRoutes(config)!;
        caddyRoutes.Count.ShouldBe(2);

        var appRoute = caddyRoutes[1]!;
        appRoute["@id"]!.GetValue<string>().ShouldBe("route_my-app");
        appRoute["terminal"]!.GetValue<bool>().ShouldBeTrue();

        var host = appRoute["match"]![0]!["host"]![0]!.GetValue<string>();
        host.ShouldBe("my-app.collab.internal");

        var handler = appRoute["handle"]![0]!;
        handler["handler"]!.GetValue<string>().ShouldBe("reverse_proxy");

        var dial = handler["upstreams"]![0]!["dial"]!.GetValue<string>();
        dial.ShouldBe("localhost:5000");
    }

    [Fact]
    public void Build_ReverseProxyRoute_NullPort_DialsFallbackZero()
    {
        var routes = new List<RouteEntry>
        {
            new("my-app", DefaultDomain("my-app"), ServeMode.ReverseProxy, Port: null, SpaFallback: false, ArtifactDirectory: null, Enabled: true)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var appRoute = GetRoutes(config)![1]!;
        var dial = appRoute["handle"]![0]!["upstreams"]![0]!["dial"]!.GetValue<string>();
        dial.ShouldBe("localhost:0");
    }

    [Fact]
    public void Build_FileServerRoute_HasSubrouteWithFileServer()
    {
        var routes = new List<RouteEntry>
        {
            new("docs", DefaultDomain("docs"), ServeMode.FileServer, Port: null, SpaFallback: false, ArtifactDirectory: "/srv/docs", Enabled: true)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var appRoute = GetRoutes(config)![1]!;
        appRoute["@id"]!.GetValue<string>().ShouldBe("route_docs");

        var subroute = appRoute["handle"]![0]!;
        subroute["handler"]!.GetValue<string>().ShouldBe("subroute");

        var subrouteHandlers = subroute["routes"]!.AsArray();
        subrouteHandlers.Count.ShouldBe(2);

        // First handler sets root
        var rootHandler = subrouteHandlers[0]!["handle"]![0]!;
        rootHandler["handler"]!.GetValue<string>().ShouldBe("vars");
        rootHandler["root"]!.GetValue<string>().ShouldBe("/srv/docs");

        // Second handler is file_server
        var fileHandler = subrouteHandlers[1]!["handle"]![0]!;
        fileHandler["handler"]!.GetValue<string>().ShouldBe("file_server");
    }

    [Fact]
    public void Build_FileServerRoute_WithSpaFallback_IncludesTryFilesRewrite()
    {
        var routes = new List<RouteEntry>
        {
            new("spa", DefaultDomain("spa"), ServeMode.FileServer, Port: null, SpaFallback: true, ArtifactDirectory: "/srv/spa", Enabled: true)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var appRoute = GetRoutes(config)![1]!;

        var subrouteHandlers = appRoute["handle"]![0]!["routes"]!.AsArray();
        subrouteHandlers.Count.ShouldBe(3);

        // Second handler is the SPA fallback rewrite
        var spaHandler = subrouteHandlers[1]!;
        var tryFiles = spaHandler["match"]![0]!["file"]!["try_files"]!.AsArray();
        tryFiles.Count.ShouldBe(2);
        tryFiles[0]!.GetValue<string>().ShouldBe("{http.request.uri.path}");
        tryFiles[1]!.GetValue<string>().ShouldBe("/index.html");

        var rewrite = spaHandler["handle"]![0]!;
        rewrite["handler"]!.GetValue<string>().ShouldBe("rewrite");
    }

    [Fact]
    public void Build_DisabledRoute_Returns503StaticResponse()
    {
        var routes = new List<RouteEntry>
        {
            new("offline", DefaultDomain("offline"), ServeMode.ReverseProxy, Port: 5000, SpaFallback: false, ArtifactDirectory: null, Enabled: false)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var appRoute = GetRoutes(config)![1]!;
        appRoute["@id"]!.GetValue<string>().ShouldBe("route_offline");

        var handler = appRoute["handle"]![0]!;
        handler["handler"]!.GetValue<string>().ShouldBe("static_response");
        handler["status_code"]!.GetValue<string>().ShouldBe("503");
        handler["body"]!.GetValue<string>().ShouldBe("Service Unavailable");
    }

    [Fact]
    public void Build_TlsConfig_IncludesAllSubjects()
    {
        var routes = new List<RouteEntry>
        {
            new("app-a", DefaultDomain("app-a"), ServeMode.ReverseProxy, Port: 3000, SpaFallback: false, ArtifactDirectory: null, Enabled: true),
            new("app-b", DefaultDomain("app-b"), ServeMode.FileServer, Port: null, SpaFallback: false, ArtifactDirectory: "/srv/b", Enabled: true)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var subjects = config["apps"]!["tls"]!["automation"]!["policies"]![0]!["subjects"]!.AsArray();
        subjects.Count.ShouldBe(3);
        subjects[0]!.GetValue<string>().ShouldBe("collabhost.collab.internal");
        subjects[1]!.GetValue<string>().ShouldBe("app-a.collab.internal");
        subjects[2]!.GetValue<string>().ShouldBe("app-b.collab.internal");
    }

    [Fact]
    public void Build_InternalBranch_PkiConfig_HasLocalAuthority()
    {
        // Predicate: internal-CA branch only. The ACME branch (Build_RealDomain_DoesNotEmitPkiBlock)
        // asserts the inverse for completeness.
        var config = ProxyConfigurationBuilder.Build([], _defaultSettings, _defaultHosting, _defaultPortal);

        var pki = config["apps"]!["pki"]!;
        var ca = pki["certificate_authorities"]!["local"]!;
        ca["name"]!.GetValue<string>().ShouldBe("Collabhost Local Authority");
        ca["install_trust"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public void Build_HttpConfig_UsesSettingsListenAddress()
    {
        var config = ProxyConfigurationBuilder.Build([], _defaultSettings, _defaultHosting, _defaultPortal);

        var listen = config["apps"]!["http"]!["servers"]!["srv0"]!["listen"]![0]!.GetValue<string>();
        listen.ShouldBe(":443");
    }

    [Fact]
    public void Build_AdminConfig_UsesAllocatedAdminPort()
    {
        var settings = new ProxySettings
        {
            BaseDomain = "collab.internal",
            BinaryPath = "caddy",
            ListenAddress = ":443",
            CertLifetime = "168h",
            AdminPort = 9876
        };

        var config = ProxyConfigurationBuilder.Build([], settings, _defaultHosting, _defaultPortal);

        var adminListen = config["admin"]!["listen"]!.GetValue<string>();
        adminListen.ShouldBe("localhost:9876");
    }

    [Fact]
    public void Build_CustomBaseDomain_ReflectedInRoutes()
    {
        var settings = new ProxySettings
        {
            BaseDomain = "mylab.local",
            BinaryPath = "caddy",
            ListenAddress = ":443",
            CertLifetime = "168h",
            AdminPort = 2019
        };

        var hosting = new HostingSettings { ListenAddress = "localhost", ListenPort = 9000 };

        var routes = new List<RouteEntry>
        {
            new("test-app", DefaultDomain("test-app", "mylab.local"), ServeMode.ReverseProxy, Port: 8080, SpaFallback: false, ArtifactDirectory: null, Enabled: true)
        };

        var config = ProxyConfigurationBuilder.Build(routes, settings, hosting, _defaultPortal);

        var selfHost = GetRoutes(config)![0]!["match"]![0]!["host"]![0]!.GetValue<string>();
        selfHost.ShouldBe("collabhost.mylab.local");

        var appHost = GetRoutes(config)![1]!["match"]![0]!["host"]![0]!.GetValue<string>();
        appHost.ShouldBe("test-app.mylab.local");
    }

    [Fact]
    public void Build_MultipleRoutes_AllIncludedWithIdTags()
    {
        var routes = new List<RouteEntry>
        {
            new("alpha", DefaultDomain("alpha"), ServeMode.ReverseProxy, Port: 3000, SpaFallback: false, ArtifactDirectory: null, Enabled: true),
            new("bravo", DefaultDomain("bravo"), ServeMode.ReverseProxy, Port: 3001, SpaFallback: false, ArtifactDirectory: null, Enabled: true),
            new("charlie", DefaultDomain("charlie"), ServeMode.FileServer, Port: null, SpaFallback: true, ArtifactDirectory: "/srv/c", Enabled: true)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var caddyRoutes = GetRoutes(config)!;
        caddyRoutes.Count.ShouldBe(4);

        caddyRoutes[0]!["@id"]!.GetValue<string>().ShouldBe("route_collabhost");
        caddyRoutes[1]!["@id"]!.GetValue<string>().ShouldBe("route_alpha");
        caddyRoutes[2]!["@id"]!.GetValue<string>().ShouldBe("route_bravo");
        caddyRoutes[3]!["@id"]!.GetValue<string>().ShouldBe("route_charlie");
    }

    [Fact]
    public void Build_DisabledFileServerRoute_Returns503NotFileServer()
    {
        var routes = new List<RouteEntry>
        {
            new("static-site", DefaultDomain("static-site"), ServeMode.FileServer, Port: null, SpaFallback: true, ArtifactDirectory: "/srv/site", Enabled: false)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var appRoute = GetRoutes(config)![1]!;
        appRoute["@id"]!.GetValue<string>().ShouldBe("route_static-site");

        var handler = appRoute["handle"]![0]!;
        handler["handler"]!.GetValue<string>().ShouldBe("static_response");
        handler["status_code"]!.GetValue<string>().ShouldBe("503");
    }

    // ----- Card #308: per-path response-header control on static-site -----

    [Fact]
    public void Build_FileServerRoute_NoResponseHeaders_ShapeUnchanged()
    {
        // Migration-safe default: null ResponseHeaders emits the identical
        // pre-#308 subroute shape (vars root + file_server, count 2). This is
        // the invariant that keeps every existing file-server route untouched.
        var routes = new List<RouteEntry>
        {
            new("docs", DefaultDomain("docs"), ServeMode.FileServer, Port: null, SpaFallback: false, ArtifactDirectory: "/srv/docs", Enabled: true, ResponseHeaders: null)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var subrouteHandlers = GetRoutes(config)![1]!["handle"]![0]!["routes"]!.AsArray();
        subrouteHandlers.Count.ShouldBe(2);
        subrouteHandlers[1]!["handle"]![0]!["handler"]!.GetValue<string>().ShouldBe("file_server");
    }

    [Fact]
    public void Build_FileServerRoute_EmptyResponseHeaders_ShapeUnchanged()
    {
        var routes = new List<RouteEntry>
        {
            new("docs", DefaultDomain("docs"), ServeMode.FileServer, Port: null, SpaFallback: false, ArtifactDirectory: "/srv/docs", Enabled: true, ResponseHeaders: new Dictionary<string, string>(StringComparer.Ordinal))
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var subrouteHandlers = GetRoutes(config)![1]!["handle"]![0]!["routes"]!.AsArray();
        subrouteHandlers.Count.ShouldBe(2);
    }

    [Fact]
    public void Build_FileServerRoute_ConfigJsonNoCache_EmitsPathScopedHeadersHandlerAtIndex1()
    {
        var routes = new List<RouteEntry>
        {
            new
            (
                "portal",
                DefaultDomain("portal"),
                ServeMode.FileServer,
                Port: null,
                SpaFallback: false,
                ArtifactDirectory: "/srv/portal",
                Enabled: true,
                ResponseHeaders: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["/config.json::Cache-Control"] = "no-cache"
                }
            )
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var subrouteHandlers = GetRoutes(config)![1]!["handle"]![0]!["routes"]!.AsArray();

        // 0 = vars root, 1 = headers handler, 2 = file_server
        subrouteHandlers.Count.ShouldBe(3);
        subrouteHandlers[0]!["handle"]![0]!["handler"]!.GetValue<string>().ShouldBe("vars");
        subrouteHandlers[2]!["handle"]![0]!["handler"]!.GetValue<string>().ShouldBe("file_server");

        var headerEntry = subrouteHandlers[1]!;

        // Path-scoped match, never blanket.
        headerEntry["match"]![0]!["path"]![0]!.GetValue<string>().ShouldBe("/config.json");

        var headerHandler = headerEntry["handle"]![0]!;
        headerHandler["handler"]!.GetValue<string>().ShouldBe("headers");
        headerHandler["response"]!["set"]!["Cache-Control"]![0]!.GetValue<string>().ShouldBe("no-cache");
    }

    [Fact]
    public void Build_FileServerRoute_HeaderHandlerBeforeSpaRewriteAndFileServer()
    {
        var routes = new List<RouteEntry>
        {
            new
            (
                "spa",
                DefaultDomain("spa"),
                ServeMode.FileServer,
                Port: null,
                SpaFallback: true,
                ArtifactDirectory: "/srv/spa",
                Enabled: true,
                ResponseHeaders: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["/config.json::Cache-Control"] = "no-cache"
                }
            )
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var subrouteHandlers = GetRoutes(config)![1]!["handle"]![0]!["routes"]!.AsArray();

        // 0 = vars root, 1 = headers, 2 = SPA rewrite, 3 = file_server
        subrouteHandlers.Count.ShouldBe(4);
        subrouteHandlers[1]!["handle"]![0]!["handler"]!.GetValue<string>().ShouldBe("headers");
        subrouteHandlers[2]!["handle"]![0]!["handler"]!.GetValue<string>().ShouldBe("rewrite");
        subrouteHandlers[3]!["handle"]![0]!["handler"]!.GetValue<string>().ShouldBe("file_server");
    }

    [Fact]
    public void Build_FileServerRoute_HashedAsset_GetsNoCacheControl()
    {
        // The path match is scoped: only /config.json gets the header. A
        // content-hashed asset must not receive Cache-Control (it caches
        // forever). Proven by the single path-scoped match -- there is no
        // blanket handler.
        var routes = new List<RouteEntry>
        {
            new
            (
                "portal",
                DefaultDomain("portal"),
                ServeMode.FileServer,
                Port: null,
                SpaFallback: false,
                ArtifactDirectory: "/srv/portal",
                Enabled: true,
                ResponseHeaders: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["/config.json::Cache-Control"] = "no-cache"
                }
            )
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var subrouteHandlers = GetRoutes(config)![1]!["handle"]![0]!["routes"]!.AsArray();

        var headerEntries = HeaderHandlerEntries(subrouteHandlers);

        headerEntries.Count.ShouldBe(1);
        headerEntries[0]!["match"]![0]!["path"]![0]!.GetValue<string>().ShouldBe("/config.json");
        // The single headers handler is path-scoped, never blanket.
        headerEntries[0]!["match"].ShouldNotBeNull();
    }

    [Fact]
    public void Build_FileServerRoute_MultipleHeadersSamePath_GroupedIntoOneHandler()
    {
        var routes = new List<RouteEntry>
        {
            new
            (
                "portal",
                DefaultDomain("portal"),
                ServeMode.FileServer,
                Port: null,
                SpaFallback: false,
                ArtifactDirectory: "/srv/portal",
                Enabled: true,
                ResponseHeaders: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["/config.json::Cache-Control"] = "no-cache",
                    ["/config.json::X-Config-Source"] = "collabhost"
                }
            )
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var subrouteHandlers = GetRoutes(config)![1]!["handle"]![0]!["routes"]!.AsArray();

        var headerEntries = HeaderHandlerEntries(subrouteHandlers);

        headerEntries.Count.ShouldBe(1);

        var set = headerEntries[0]!["handle"]![0]!["response"]!["set"]!.AsObject();
        set["Cache-Control"]![0]!.GetValue<string>().ShouldBe("no-cache");
        set["X-Config-Source"]![0]!.GetValue<string>().ShouldBe("collabhost");
    }

    [Fact]
    public void Build_FileServerRoute_HeadersForDistinctPaths_EmitsOneHandlerPerPath()
    {
        var routes = new List<RouteEntry>
        {
            new
            (
                "portal",
                DefaultDomain("portal"),
                ServeMode.FileServer,
                Port: null,
                SpaFallback: false,
                ArtifactDirectory: "/srv/portal",
                Enabled: true,
                ResponseHeaders: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["/config.json::Cache-Control"] = "no-cache",
                    ["/manifest.json::Cache-Control"] = "max-age=3600"
                }
            )
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var subrouteHandlers = GetRoutes(config)![1]!["handle"]![0]!["routes"]!.AsArray();

        var headerEntries = HeaderHandlerEntries(subrouteHandlers);

        headerEntries.Count.ShouldBe(2);
        // Deterministic ordinal-by-path ordering.
        headerEntries[0]!["match"]![0]!["path"]![0]!.GetValue<string>().ShouldBe("/config.json");
        headerEntries[1]!["match"]![0]!["path"]![0]!.GetValue<string>().ShouldBe("/manifest.json");
    }

    [Fact]
    public void Build_FileServerRoute_MalformedHeaderKey_SkippedDefensively()
    {
        // ValidateEdits is the authoritative gate; the builder must still never
        // emit invalid Caddy config for a legacy/seed key that slips through.
        var routes = new List<RouteEntry>
        {
            new
            (
                "portal",
                DefaultDomain("portal"),
                ServeMode.FileServer,
                Port: null,
                SpaFallback: false,
                ArtifactDirectory: "/srv/portal",
                Enabled: true,
                ResponseHeaders: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["no-separator-here"] = "value",
                    ["relative/path::Cache-Control"] = "no-cache",
                    ["/config.json::Cache-Control"] = "no-cache"
                }
            )
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var subrouteHandlers = GetRoutes(config)![1]!["handle"]![0]!["routes"]!.AsArray();

        var headerEntries = HeaderHandlerEntries(subrouteHandlers);

        // Only the well-formed /config.json rule survives.
        headerEntries.Count.ShouldBe(1);
        headerEntries[0]!["match"]![0]!["path"]![0]!.GetValue<string>().ShouldBe("/config.json");
    }

    [Fact]
    public void Build_ReverseProxyRoute_ResponseHeadersIgnored()
    {
        // ResponseHeaders is a file-server concept (schema DependsOn
        // serveMode=FileServer). A reverse-proxy route must not grow a headers
        // handler even if the slot is populated.
        var routes = new List<RouteEntry>
        {
            new
            (
                "api",
                DefaultDomain("api"),
                ServeMode.ReverseProxy,
                Port: 5000,
                SpaFallback: false,
                ArtifactDirectory: null,
                Enabled: true,
                ResponseHeaders: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["/config.json::Cache-Control"] = "no-cache"
                }
            )
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var handler = GetRoutes(config)![1]!["handle"]![0]!;
        handler["handler"]!.GetValue<string>().ShouldBe("reverse_proxy");
    }

    [Fact]
    public void Build_InternalBranch_CertLifetime_ReflectedInTlsConfig()
    {
        // Predicate: lifetime is internal-CA-only. Let's Encrypt sets 90 days
        // and rejects custom lifetimes, so the ACME branch (see
        // Build_RealDomain_DoesNotEmitLifetime) does not emit this field.
        var settings = new ProxySettings
        {
            BaseDomain = "collab.internal",
            BinaryPath = "caddy",
            ListenAddress = ":443",
            CertLifetime = "720h",
            AdminPort = 2019
        };

        var config = ProxyConfigurationBuilder.Build([], settings, _defaultHosting, _defaultPortal);

        var lifetime = config["apps"]!["tls"]!["automation"]!["policies"]![0]!["issuers"]![0]!["lifetime"]!.GetValue<string>();
        lifetime.ShouldBe("720h");
    }

    [Fact]
    public void Build_AllRoutesTerminal()
    {
        var routes = new List<RouteEntry>
        {
            new("test", DefaultDomain("test"), ServeMode.ReverseProxy, Port: 5000, SpaFallback: false, ArtifactDirectory: null, Enabled: true)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var caddyRoutes = GetRoutes(config)!;

        foreach (var route in caddyRoutes)
        {
            route!["terminal"]!.GetValue<bool>().ShouldBeTrue();
        }
    }

    // ----- Card #184: Portal:Subdomain knob threaded through self-route -----

    [Fact]
    public void Build_DefaultPortalSubdomain_ProducesCollabhostHost()
    {
        var config = ProxyConfigurationBuilder.Build([], _defaultSettings, _defaultHosting, _defaultPortal);

        var selfHost = GetRoutes(config)![0]!["match"]![0]!["host"]![0]!.GetValue<string>();
        selfHost.ShouldBe("collabhost.collab.internal");
    }

    [Fact]
    public void Build_CustomPortalSubdomain_ChangesSelfRouteHostAndTlsSubject()
    {
        var portal = new PortalSettings { Subdomain = "portal" };
        var settings = new ProxySettings
        {
            BaseDomain = "collabot.dev",
            BinaryPath = "caddy",
            ListenAddress = ":443",
            CertLifetime = "168h",
            AdminPort = 2019
        };

        var config = ProxyConfigurationBuilder.Build([], settings, _defaultHosting, portal);

        var selfHost = GetRoutes(config)![0]!["match"]![0]!["host"]![0]!.GetValue<string>();
        selfHost.ShouldBe("portal.collabot.dev");

        var subjects = config["apps"]!["tls"]!["automation"]!["policies"]![0]!["subjects"]!.AsArray();
        subjects[0]!.GetValue<string>().ShouldBe("portal.collabot.dev");
    }

    // ----- Card #184: RouteEntry.Domain consumed by per-app routes -----

    [Fact]
    public void Build_RouteEntryWithCustomDomain_EmitsCustomHost()
    {
        var routes = new List<RouteEntry>
        {
            new("myapp", "console.example.com", ServeMode.ReverseProxy, Port: 5000, SpaFallback: false, ArtifactDirectory: null, Enabled: true)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var appHost = GetRoutes(config)![1]!["match"]![0]!["host"]![0]!.GetValue<string>();
        appHost.ShouldBe("console.example.com");

        var subjects = config["apps"]!["tls"]!["automation"]!["policies"]![0]!["subjects"]!.AsArray();
        subjects.ShouldContain(s => s!.GetValue<string>() == "console.example.com");
    }

    [Fact]
    public void Build_RouteEntryWithDefaultDomainPattern_EmitsSlugBaseDomain()
    {
        var routes = new List<RouteEntry>
        {
            new("myapp", "myapp.collab.internal", ServeMode.ReverseProxy, Port: 5000, SpaFallback: false, ArtifactDirectory: null, Enabled: true)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var appHost = GetRoutes(config)![1]!["match"]![0]!["host"]![0]!.GetValue<string>();
        appHost.ShouldBe("myapp.collab.internal");
    }

    [Fact]
    public void Build_DisabledRoute_UsesRouteEntryDomain()
    {
        var routes = new List<RouteEntry>
        {
            new("weird", "weird.example.com", ServeMode.ReverseProxy, Port: 5000, SpaFallback: false, ArtifactDirectory: null, Enabled: false)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var appHost = GetRoutes(config)![1]!["match"]![0]!["host"]![0]!.GetValue<string>();
        appHost.ShouldBe("weird.example.com");
    }

    [Fact]
    public void Build_FileServerRoute_UsesRouteEntryDomain()
    {
        var routes = new List<RouteEntry>
        {
            new("docs", "docs.example.com", ServeMode.FileServer, Port: null, SpaFallback: false, ArtifactDirectory: "/srv/docs", Enabled: true)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var appHost = GetRoutes(config)![1]!["match"]![0]!["host"]![0]!.GetValue<string>();
        appHost.ShouldBe("docs.example.com");
    }

    // ----- Card #34 Phase C: TLS issuer branching (internal CA vs ACME DNS-01) -----

    [Fact]
    public void Build_InternalDomain_UsesInternalIssuer()
    {
        // Default settings have DnsProvider unset -- exercises the internal-CA branch.
        var config = ProxyConfigurationBuilder.Build([], _defaultSettings, _defaultHosting, _defaultPortal);

        var issuer = config["apps"]!["tls"]!["automation"]!["policies"]![0]!["issuers"]![0]!;

        issuer["module"]!.GetValue<string>().ShouldBe("internal");
        issuer["ca"]!.GetValue<string>().ShouldBe("local");
    }

    [Fact]
    public void Build_RealDomain_UsesAcmeIssuer_WithCloudflareDnsChallenge()
    {
        var settings = new ProxySettings
        {
            BaseDomain = "collabot.dev",
            BinaryPath = "caddy",
            ListenAddress = ":443",
            CertLifetime = "168h",
            DnsProvider = "cloudflare",
            DnsApiTokenEnvVar = "CLOUDFLARE_API_TOKEN",
            AdminPort = 2019
        };

        var config = ProxyConfigurationBuilder.Build([], settings, _defaultHosting, _defaultPortal);

        var issuer = config["apps"]!["tls"]!["automation"]!["policies"]![0]!["issuers"]![0]!;

        issuer["module"]!.GetValue<string>().ShouldBe("acme");

        var provider = issuer["challenges"]!["dns"]!["provider"]!;
        provider["name"]!.GetValue<string>().ShouldBe("cloudflare");
        provider["api_token"]!.GetValue<string>().ShouldBe("{env.CLOUDFLARE_API_TOKEN}");
    }

    [Fact]
    public void Build_RealDomain_UsesConfiguredApiTokenEnvVar()
    {
        // Custom env-var name flows through the placeholder unchanged.
        var settings = new ProxySettings
        {
            BaseDomain = "collabot.dev",
            BinaryPath = "caddy",
            ListenAddress = ":443",
            CertLifetime = "168h",
            DnsProvider = "cloudflare",
            DnsApiTokenEnvVar = "MY_CF_TOKEN",
            AdminPort = 2019
        };

        var config = ProxyConfigurationBuilder.Build([], settings, _defaultHosting, _defaultPortal);

        var apiToken = config["apps"]!["tls"]!["automation"]!["policies"]![0]!["issuers"]![0]!
            ["challenges"]!["dns"]!["provider"]!["api_token"]!.GetValue<string>();

        apiToken.ShouldBe("{env.MY_CF_TOKEN}");
    }

    [Fact]
    public void Build_RealDomain_DoesNotEmitLifetime()
    {
        // Let's Encrypt rejects custom lifetimes -- ACME branch must omit the field
        // even when CertLifetime is set in settings.
        var settings = new ProxySettings
        {
            BaseDomain = "collabot.dev",
            BinaryPath = "caddy",
            ListenAddress = ":443",
            CertLifetime = "720h",
            DnsProvider = "cloudflare",
            DnsApiTokenEnvVar = "CLOUDFLARE_API_TOKEN",
            AdminPort = 2019
        };

        var config = ProxyConfigurationBuilder.Build([], settings, _defaultHosting, _defaultPortal);

        var issuer = config["apps"]!["tls"]!["automation"]!["policies"]![0]!["issuers"]![0]!.AsObject();

        issuer.ContainsKey("lifetime").ShouldBeFalse();
    }

    [Fact]
    public void Build_RealDomain_DoesNotEmitPkiBlock()
    {
        // Caddy ignores the local pki block under ACME -- omit it as dead weight.
        var settings = new ProxySettings
        {
            BaseDomain = "collabot.dev",
            BinaryPath = "caddy",
            ListenAddress = ":443",
            CertLifetime = "168h",
            DnsProvider = "cloudflare",
            DnsApiTokenEnvVar = "CLOUDFLARE_API_TOKEN",
            AdminPort = 2019
        };

        var config = ProxyConfigurationBuilder.Build([], settings, _defaultHosting, _defaultPortal);

        var apps = config["apps"]!.AsObject();

        apps.ContainsKey("pki").ShouldBeFalse();
    }

    // ----- Card #230 phase 1: Caddy storage block (operator-controlled path) -----

    [Fact]
    public void Build_StoragePathUnset_OmitsStorageBlock()
    {
        // Default settings have StoragePath unset -- Caddy uses its built-in default
        // (XDG_DATA_HOME / %AppData% / ~/Library). Additive contract for v1.0.x: an
        // operator who hasn't touched the new key must see no behavior change.
        var config = ProxyConfigurationBuilder.Build([], _defaultSettings, _defaultHosting, _defaultPortal);

        config.AsObject().ContainsKey("storage").ShouldBeFalse();
    }

    [Fact]
    public void Build_StoragePathSet_EmitsFileSystemStorageBlock()
    {
        var settings = new ProxySettings
        {
            BaseDomain = "collab.internal",
            BinaryPath = "caddy",
            ListenAddress = ":443",
            CertLifetime = "168h",
            StoragePath = "/var/lib/collabhost/caddy",
            AdminPort = 2019
        };

        var config = ProxyConfigurationBuilder.Build([], settings, _defaultHosting, _defaultPortal);

        var storage = config["storage"]!;

        storage["module"]!.GetValue<string>().ShouldBe("file_system");
        storage["root"]!.GetValue<string>().ShouldBe("/var/lib/collabhost/caddy");
    }

    [Fact]
    public void Build_StoragePathWhitespace_OmitsStorageBlock()
    {
        // Whitespace-only treated as unset (consistent with the env-var blank-is-unset
        // contract in ProxyRegistration.ResolveSettings).
        var settings = new ProxySettings
        {
            BaseDomain = "collab.internal",
            BinaryPath = "caddy",
            ListenAddress = ":443",
            CertLifetime = "168h",
            StoragePath = "   ",
            AdminPort = 2019
        };

        var config = ProxyConfigurationBuilder.Build([], settings, _defaultHosting, _defaultPortal);

        config.AsObject().ContainsKey("storage").ShouldBeFalse();
    }

    [Fact]
    public void Build_StoragePathSet_DoesNotAffectAppsBlock()
    {
        // Sanity check: storage is a top-level Caddy concept, not under apps.
        var settings = new ProxySettings
        {
            BaseDomain = "collab.internal",
            BinaryPath = "caddy",
            ListenAddress = ":443",
            CertLifetime = "168h",
            StoragePath = "/var/lib/collabhost/caddy",
            AdminPort = 2019
        };

        var config = ProxyConfigurationBuilder.Build([], settings, _defaultHosting, _defaultPortal);

        config["apps"]!.AsObject().ContainsKey("storage").ShouldBeFalse();
    }

    private static JsonArray? GetRoutes(JsonObject config) =>
        config["apps"]?["http"]?["servers"]?["srv0"]?["routes"]?.AsArray();

    // Card #308: materialize the `headers`-handler subroute entries via an
    // explicit loop. Avoids LINQ-over-JsonNode (MA0002 wants a comparer) and
    // Shouldly expression-tree predicate overloads (CS8072/CS8122 on ?. / is).
    private static List<JsonNode> HeaderHandlerEntries(JsonArray subrouteHandlers)
    {
        var entries = new List<JsonNode>();

        foreach (var handler in subrouteHandlers)
        {
            if (handler is null)
            {
                continue;
            }

            var name = handler["handle"]?[0]?["handler"]?.GetValue<string>();

            if (string.Equals(name, "headers", StringComparison.Ordinal))
            {
                entries.Add(handler);
            }
        }

        return entries;
    }

    // ----- Card #309: security-headers blanket emission -----

    [Fact]
    public void Build_ReverseProxyRoute_NullSecurityHeaders_FlatShapeUnchanged()
    {
        // Migration safety: a route with no security-headers spec (capability
        // not bound, or the empty-no-op invariant fires) emits the byte-
        // identical pre-#309 flat reverse-proxy shape -- no subroute wrap.
        // Precondition #1 + #6: invariant at the builder.
        var routes = new List<RouteEntry>
        {
            new("api", DefaultDomain("api"), ServeMode.ReverseProxy, Port: 5000, SpaFallback: false, ArtifactDirectory: null, Enabled: true, ResponseHeaders: null, SecurityHeaders: null)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var handler = GetRoutes(config)![1]!["handle"]![0]!;

        // Flat shape: handler is the reverse_proxy directly, NOT a subroute.
        handler["handler"]!.GetValue<string>().ShouldBe("reverse_proxy");
    }

    [Fact]
    public void Build_ReverseProxyRoute_EmptySecurityHeadersSpec_FlatShapeUnchanged()
    {
        // Empty-no-op invariant fires at the builder on the resolved spec:
        // EnableHsts: false + Headers: {} -> no emission, no subroute wrap.
        // Precondition #1: mirrors #336's RuntimeConfigFileWriter empty-Values
        // short-circuit.
        var spec = new SecurityHeadersConfiguration
        {
            EnableHsts = false,
            Headers = new Dictionary<string, string>(StringComparer.Ordinal)
        };

        var routes = new List<RouteEntry>
        {
            new("api", DefaultDomain("api"), ServeMode.ReverseProxy, Port: 5000, SpaFallback: false, ArtifactDirectory: null, Enabled: true, ResponseHeaders: null, SecurityHeaders: spec)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var handler = GetRoutes(config)![1]!["handle"]![0]!;

        handler["handler"]!.GetValue<string>().ShouldBe("reverse_proxy");
    }

    [Fact]
    public void Build_ReverseProxyRoute_AllHeadersSuppressed_FlatShapeUnchanged()
    {
        // Operator-suppression case: every Headers entry has empty value AND
        // EnableHsts: false. The emission helper drops every entry, the
        // post-suppression set is empty, and emission returns null -- the
        // route stays flat. Precondition #1 + #5 + #6 in combination.
        var spec = new SecurityHeadersConfiguration
        {
            EnableHsts = false,
            Headers = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["X-Content-Type-Options"] = ""
            }
        };

        var routes = new List<RouteEntry>
        {
            new("api", DefaultDomain("api"), ServeMode.ReverseProxy, Port: 5000, SpaFallback: false, ArtifactDirectory: null, Enabled: true, ResponseHeaders: null, SecurityHeaders: spec)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var handler = GetRoutes(config)![1]!["handle"]![0]!;

        handler["handler"]!.GetValue<string>().ShouldBe("reverse_proxy");
    }

    [Fact]
    public void Build_ReverseProxyRoute_XctoSeed_WrappedInSubrouteWithHeadersBeforeReverseProxy()
    {
        // The XCTO-default-on case: every routed app type's security-headers
        // type-level default carries {"X-Content-Type-Options": "nosniff"}.
        // The emitted reverse-proxy route gains a subroute wrap with the
        // blanket headers handler BEFORE the reverse_proxy. Precondition #6.
        var spec = new SecurityHeadersConfiguration
        {
            EnableHsts = false,
            Headers = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["X-Content-Type-Options"] = "nosniff"
            }
        };

        var routes = new List<RouteEntry>
        {
            new("api", DefaultDomain("api"), ServeMode.ReverseProxy, Port: 5000, SpaFallback: false, ArtifactDirectory: null, Enabled: true, ResponseHeaders: null, SecurityHeaders: spec)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var topHandler = GetRoutes(config)![1]!["handle"]![0]!;

        topHandler["handler"]!.GetValue<string>().ShouldBe("subroute");

        var subrouteHandlers = topHandler["routes"]!.AsArray();
        subrouteHandlers.Count.ShouldBe(2);

        var headers = subrouteHandlers[0]!["handle"]![0]!;
        headers["handler"]!.GetValue<string>().ShouldBe("headers");
        headers["response"]!["set"]!["X-Content-Type-Options"]![0]!.GetValue<string>().ShouldBe("nosniff");

        var reverseProxy = subrouteHandlers[1]!["handle"]![0]!;
        reverseProxy["handler"]!.GetValue<string>().ShouldBe("reverse_proxy");
    }

    [Fact]
    public void Build_ReverseProxyRoute_HstsEnabled_EmitsStsHeader()
    {
        // EnableHsts: true expands to a Strict-Transport-Security entry at
        // emission time, computed from HstsMaxAgeSeconds. The default of 300
        // (5 minutes) is the staged-rollout floor.
        var spec = new SecurityHeadersConfiguration
        {
            EnableHsts = true,
            HstsMaxAgeSeconds = 300,
            Headers = new Dictionary<string, string>(StringComparer.Ordinal)
        };

        var routes = new List<RouteEntry>
        {
            new("api", DefaultDomain("api"), ServeMode.ReverseProxy, Port: 5000, SpaFallback: false, ArtifactDirectory: null, Enabled: true, ResponseHeaders: null, SecurityHeaders: spec)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var subrouteHandlers = GetRoutes(config)![1]!["handle"]![0]!["routes"]!.AsArray();

        var headers = subrouteHandlers[0]!["handle"]![0]!;
        headers["response"]!["set"]!["Strict-Transport-Security"]![0]!.GetValue<string>().ShouldBe("max-age=300");
    }

    [Fact]
    public void Build_ReverseProxyRoute_HstsMaxAgeZero_EmitsMaxAgeZero()
    {
        // The rollback / un-pin signal: EnableHsts: true + HstsMaxAgeSeconds: 0
        // emits "max-age=0", which tells browsers to expire any prior pin.
        // Precondition #4: zero is allowed and is the documented escape hatch.
        var spec = new SecurityHeadersConfiguration
        {
            EnableHsts = true,
            HstsMaxAgeSeconds = 0,
            Headers = new Dictionary<string, string>(StringComparer.Ordinal)
        };

        var routes = new List<RouteEntry>
        {
            new("api", DefaultDomain("api"), ServeMode.ReverseProxy, Port: 5000, SpaFallback: false, ArtifactDirectory: null, Enabled: true, ResponseHeaders: null, SecurityHeaders: spec)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var subrouteHandlers = GetRoutes(config)![1]!["handle"]![0]!["routes"]!.AsArray();

        var headers = subrouteHandlers[0]!["handle"]![0]!;
        headers["response"]!["set"]!["Strict-Transport-Security"]![0]!.GetValue<string>().ShouldBe("max-age=0");
    }

    [Fact]
    public void Build_ReverseProxyRoute_EmptyValueHeader_DroppedFromEmission()
    {
        // Operator-suppression mechanism (Bill ruling, precondition #5):
        // an entry with empty-string value is the documented escape hatch for
        // a type-default. The emission helper drops it; the surviving headers
        // are emitted. Pairs with the all-suppressed test above.
        var spec = new SecurityHeadersConfiguration
        {
            EnableHsts = false,
            Headers = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["X-Content-Type-Options"] = "",
                ["Referrer-Policy"] = "strict-origin-when-cross-origin"
            }
        };

        var routes = new List<RouteEntry>
        {
            new("api", DefaultDomain("api"), ServeMode.ReverseProxy, Port: 5000, SpaFallback: false, ArtifactDirectory: null, Enabled: true, ResponseHeaders: null, SecurityHeaders: spec)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var subrouteHandlers = GetRoutes(config)![1]!["handle"]![0]!["routes"]!.AsArray();

        var set = subrouteHandlers[0]!["handle"]![0]!["response"]!["set"]!.AsObject();

        // XCTO entry was dropped; Referrer-Policy survives.
        set.ContainsKey("X-Content-Type-Options").ShouldBeFalse();
        set["Referrer-Policy"]![0]!.GetValue<string>().ShouldBe("strict-origin-when-cross-origin");
    }

    [Fact]
    public void Build_FileServerRoute_XctoSeed_InsertedBeforePathMatchedHeaders()
    {
        // Subroute insertion order (precondition #7): security-headers blanket
        // handler appears BEFORE #308's path-matched handlers. Caddy
        // `response.set` is last-write-wins on the chain, so the path-matched
        // handler (encountered later) overrides the blanket on collision.
        // CSS-specificity analogy: more-specific wins.
        var spec = new SecurityHeadersConfiguration
        {
            EnableHsts = false,
            Headers = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["X-Content-Type-Options"] = "nosniff"
            }
        };

        var routes = new List<RouteEntry>
        {
            new
            (
                "portal",
                DefaultDomain("portal"),
                ServeMode.FileServer,
                Port: null,
                SpaFallback: false,
                ArtifactDirectory: "/srv/portal",
                Enabled: true,
                ResponseHeaders: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["/config.json::Cache-Control"] = "no-cache"
                },
                SecurityHeaders: spec
            )
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var subrouteHandlers = GetRoutes(config)![1]!["handle"]![0]!["routes"]!.AsArray();

        // 0 = vars root, 1 = blanket security-headers, 2 = path-matched #308, 3 = file_server
        subrouteHandlers.Count.ShouldBe(4);
        subrouteHandlers[0]!["handle"]![0]!["handler"]!.GetValue<string>().ShouldBe("vars");

        var blanket = subrouteHandlers[1]!;
        // Blanket has no `match` segment -- it applies to every response.
        blanket.AsObject().ContainsKey("match").ShouldBeFalse();
        blanket["handle"]![0]!["handler"]!.GetValue<string>().ShouldBe("headers");
        blanket["handle"]![0]!["response"]!["set"]!["X-Content-Type-Options"]![0]!
            .GetValue<string>().ShouldBe("nosniff");

        var pathMatched = subrouteHandlers[2]!;
        // Path-matched has a `match.path`.
        pathMatched["match"]![0]!["path"]![0]!.GetValue<string>().ShouldBe("/config.json");
        pathMatched["handle"]![0]!["handler"]!.GetValue<string>().ShouldBe("headers");

        subrouteHandlers[3]!["handle"]![0]!["handler"]!.GetValue<string>().ShouldBe("file_server");
    }

    [Fact]
    public void Build_FileServerRoute_PathMatchedOverlapsBlanket_PathMatchedFollowsBlanketInChain()
    {
        // Precondition #7 precedence test (operator semantic): when blanket
        // and path-matched handlers both set the SAME header name, the path-
        // matched handler must be the one encountered LATER in the chain --
        // so Caddy's `response.set` last-write-wins selects the path-scoped
        // value. We can't observe Caddy's runtime behavior in a builder unit
        // test, but we CAN assert the deterministic chain order that produces
        // it.
        var spec = new SecurityHeadersConfiguration
        {
            EnableHsts = false,
            Headers = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["X-Test"] = "blanket-value"
            }
        };

        var routes = new List<RouteEntry>
        {
            new
            (
                "portal",
                DefaultDomain("portal"),
                ServeMode.FileServer,
                Port: null,
                SpaFallback: false,
                ArtifactDirectory: "/srv/portal",
                Enabled: true,
                ResponseHeaders: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["/foo::X-Test"] = "path-value"
                },
                SecurityHeaders: spec
            )
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var subrouteHandlers = GetRoutes(config)![1]!["handle"]![0]!["routes"]!.AsArray();

        // Index 1 is the blanket; index 2 is the path-matched. Caddy walks
        // the chain in array order; the path-matched fires AFTER the blanket
        // on /foo, so its value wins. Non-matching paths (e.g. /bar) only see
        // the blanket.
        var blanket = subrouteHandlers[1]!;
        blanket.AsObject().ContainsKey("match").ShouldBeFalse();
        blanket["handle"]![0]!["response"]!["set"]!["X-Test"]![0]!.GetValue<string>().ShouldBe("blanket-value");

        var pathMatched = subrouteHandlers[2]!;
        pathMatched["match"]![0]!["path"]![0]!.GetValue<string>().ShouldBe("/foo");
        pathMatched["handle"]![0]!["response"]!["set"]!["X-Test"]![0]!.GetValue<string>().ShouldBe("path-value");
    }

    [Fact]
    public void Build_FileServerRoute_NullSecurityHeaders_ShapeUnchanged()
    {
        // Defensive: null SecurityHeaders + null ResponseHeaders preserves the
        // pre-#308 + pre-#309 minimal file-server subroute (vars + file_server).
        var routes = new List<RouteEntry>
        {
            new("docs", DefaultDomain("docs"), ServeMode.FileServer, Port: null, SpaFallback: false, ArtifactDirectory: "/srv/docs", Enabled: true, ResponseHeaders: null, SecurityHeaders: null)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var subrouteHandlers = GetRoutes(config)![1]!["handle"]![0]!["routes"]!.AsArray();
        subrouteHandlers.Count.ShouldBe(2);
    }

    [Fact]
    public void Build_FileServerRoute_SecurityHeadersOnly_NoPathMatched()
    {
        // Pure #309 path: security-headers spec emits the blanket; #308's
        // ResponseHeaders is null. Subroute is vars + blanket + file_server.
        var spec = new SecurityHeadersConfiguration
        {
            EnableHsts = false,
            Headers = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["X-Content-Type-Options"] = "nosniff"
            }
        };

        var routes = new List<RouteEntry>
        {
            new("docs", DefaultDomain("docs"), ServeMode.FileServer, Port: null, SpaFallback: false, ArtifactDirectory: "/srv/docs", Enabled: true, ResponseHeaders: null, SecurityHeaders: spec)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var subrouteHandlers = GetRoutes(config)![1]!["handle"]![0]!["routes"]!.AsArray();
        subrouteHandlers.Count.ShouldBe(3);
        subrouteHandlers[0]!["handle"]![0]!["handler"]!.GetValue<string>().ShouldBe("vars");
        subrouteHandlers[1]!["handle"]![0]!["handler"]!.GetValue<string>().ShouldBe("headers");
        subrouteHandlers[2]!["handle"]![0]!["handler"]!.GetValue<string>().ShouldBe("file_server");
    }

    // -------- HasTlsListener (Card #263 item 1.3) --------

    [Theory]
    [InlineData(":443", true)]
    [InlineData(":80,:443", true)]
    [InlineData(" :80 , :443 ", true)]
    [InlineData("0.0.0.0:443", true)]
    [InlineData(":80", false)]
    [InlineData(":8080", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void HasTlsListener_VariousAddresses_ReturnsExpected(string listenAddress, bool expected) =>
        ProxyConfigurationBuilder.HasTlsListener(listenAddress).ShouldBe(expected);

    // -------- External-target dial (Card #348) --------

    [Fact]
    public void Build_ExternalRoute_DialsTheOperatorDeclaredAddress()
    {
        var routes = new List<RouteEntry>
        {
            new
            (
                "crawl4ai",
                DefaultDomain("crawl4ai"),
                ServeMode.ReverseProxy,
                Port: null,
                SpaFallback: false,
                ArtifactDirectory: null,
                Enabled: true,
                ResponseHeaders: null,
                SecurityHeaders: null,
                ExternalDial: "192.168.1.50:11235",
                ExternalScheme: "http"
            )
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var appRoute = GetRoutes(config)![1]!;
        var dial = appRoute["handle"]![0]!["upstreams"]![0]!["dial"]!.GetValue<string>();
        dial.ShouldBe("192.168.1.50:11235");
    }

    [Fact]
    public void Build_ExternalRoute_HttpScheme_OmitsTransportBlock()
    {
        var routes = new List<RouteEntry>
        {
            new
            (
                "crawl4ai",
                DefaultDomain("crawl4ai"),
                ServeMode.ReverseProxy,
                Port: null,
                SpaFallback: false,
                ArtifactDirectory: null,
                Enabled: true,
                ResponseHeaders: null,
                SecurityHeaders: null,
                ExternalDial: "localhost:11235",
                ExternalScheme: "http"
            )
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var handler = GetRoutes(config)![1]!["handle"]![0]!;
        handler.AsObject().ContainsKey("transport").ShouldBeFalse();
    }

    [Fact]
    public void Build_ExternalRoute_HttpsScheme_EmitsTransportTlsBlock()
    {
        var routes = new List<RouteEntry>
        {
            new
            (
                "upstream",
                DefaultDomain("upstream"),
                ServeMode.ReverseProxy,
                Port: null,
                SpaFallback: false,
                ArtifactDirectory: null,
                Enabled: true,
                ResponseHeaders: null,
                SecurityHeaders: null,
                ExternalDial: "upstream.local:8443",
                ExternalScheme: "https"
            )
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var handler = GetRoutes(config)![1]!["handle"]![0]!;
        var transport = handler["transport"]!.AsObject();

        transport["protocol"]!.GetValue<string>().ShouldBe("http");
        transport["tls"]!.AsObject().Count.ShouldBe(0);
    }

    [Fact]
    public void Build_SupervisedRoute_ExternalDialNull_DialsLocalhostAsBefore()
    {
        // Regression guard: the new field defaults preserve the supervised-
        // process shape byte-identically.
        var routes = new List<RouteEntry>
        {
            new("my-app", DefaultDomain("my-app"), ServeMode.ReverseProxy, Port: 5000, SpaFallback: false, ArtifactDirectory: null, Enabled: true)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var dial = GetRoutes(config)![1]!["handle"]![0]!["upstreams"]![0]!["dial"]!.GetValue<string>();
        dial.ShouldBe("localhost:5000");
    }

    [Fact]
    public void Build_ExternalRoute_WithSecurityHeaders_WrapsInSubroute()
    {
        var routes = new List<RouteEntry>
        {
            new
            (
                "crawl4ai",
                DefaultDomain("crawl4ai"),
                ServeMode.ReverseProxy,
                Port: null,
                SpaFallback: false,
                ArtifactDirectory: null,
                Enabled: true,
                ResponseHeaders: null,
                SecurityHeaders: new SecurityHeadersConfiguration
                {
                    EnableHsts = false,
                    HstsMaxAgeSeconds = 300,
                    Headers = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["X-Content-Type-Options"] = "nosniff"
                    }
                },
                ExternalDial: "192.168.1.50:11235",
                ExternalScheme: "http"
            )
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var handler = GetRoutes(config)![1]!["handle"]![0]!;
        handler["handler"]!.GetValue<string>().ShouldBe("subroute");

        var subrouteHandlers = handler["routes"]!.AsArray();
        subrouteHandlers.Count.ShouldBe(2);

        // First handler is the security-headers blanket; second wraps the
        // reverse_proxy still dialing the external target.
        var reverseProxy = subrouteHandlers[1]!["handle"]![0]!;
        reverseProxy["handler"]!.GetValue<string>().ShouldBe("reverse_proxy");
        reverseProxy["upstreams"]![0]!["dial"]!.GetValue<string>().ShouldBe("192.168.1.50:11235");
    }

    // -------- Card #360: per-handler trusted_proxies on reverse_proxy --------

    [Fact]
    public void Build_ReverseProxyRoute_EmitsTrustedProxiesEmptyArray()
    {
        // Card #360: every reverse_proxy handler carries `trusted_proxies: []`
        // so Caddy 2.10+ honors the documented X-Forwarded-For propagation
        // contract (trust nothing inbound, set XFF from the immediate peer).
        var routes = new List<RouteEntry>
        {
            new("my-app", DefaultDomain("my-app"), ServeMode.ReverseProxy, Port: 5000, SpaFallback: false, ArtifactDirectory: null, Enabled: true)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var handler = GetRoutes(config)![1]!["handle"]![0]!;

        handler["handler"]!.GetValue<string>().ShouldBe("reverse_proxy");

        var trustedProxies = handler["trusted_proxies"]!.AsArray();
        trustedProxies.Count.ShouldBe(0);
    }

    [Fact]
    public void Build_SelfRoute_EmitsTrustedProxiesEmptyArray()
    {
        // The Portal self-route is a reverse_proxy too -- same defended-edge
        // posture applies. Card #360.
        var config = ProxyConfigurationBuilder.Build([], _defaultSettings, _defaultHosting, _defaultPortal);

        var handler = GetRoutes(config)![0]!["handle"]![0]!;

        handler["handler"]!.GetValue<string>().ShouldBe("reverse_proxy");

        var trustedProxies = handler["trusted_proxies"]!.AsArray();
        trustedProxies.Count.ShouldBe(0);
    }

    [Fact]
    public void Build_ReverseProxyRoute_WithSecurityHeaders_TrustedProxiesEmittedInsideSubroute()
    {
        // Subroute-wrap path (security-headers bound): the reverse_proxy is
        // nested inside the subroute. The trusted_proxies field must travel
        // with it. Card #360.
        var spec = new SecurityHeadersConfiguration
        {
            EnableHsts = false,
            Headers = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["X-Content-Type-Options"] = "nosniff"
            }
        };

        var routes = new List<RouteEntry>
        {
            new("api", DefaultDomain("api"), ServeMode.ReverseProxy, Port: 5000, SpaFallback: false, ArtifactDirectory: null, Enabled: true, ResponseHeaders: null, SecurityHeaders: spec)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var subrouteHandlers = GetRoutes(config)![1]!["handle"]![0]!["routes"]!.AsArray();
        var reverseProxy = subrouteHandlers[1]!["handle"]![0]!;

        reverseProxy["handler"]!.GetValue<string>().ShouldBe("reverse_proxy");

        var trustedProxies = reverseProxy["trusted_proxies"]!.AsArray();
        trustedProxies.Count.ShouldBe(0);
    }

    [Fact]
    public void Build_ExternalRoute_EmitsTrustedProxiesEmptyArray()
    {
        // External-target routes (Card #348) share the reverse_proxy emission
        // path -- same XFF contract applies. Card #360.
        var routes = new List<RouteEntry>
        {
            new
            (
                "crawl4ai",
                DefaultDomain("crawl4ai"),
                ServeMode.ReverseProxy,
                Port: null,
                SpaFallback: false,
                ArtifactDirectory: null,
                Enabled: true,
                ResponseHeaders: null,
                SecurityHeaders: null,
                ExternalDial: "192.168.1.50:11235",
                ExternalScheme: "http"
            )
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var handler = GetRoutes(config)![1]!["handle"]![0]!;

        handler["handler"]!.GetValue<string>().ShouldBe("reverse_proxy");

        var trustedProxies = handler["trusted_proxies"]!.AsArray();
        trustedProxies.Count.ShouldBe(0);
    }

    [Fact]
    public void Build_FileServerRoute_DoesNotEmitTrustedProxies()
    {
        // file_server has no upstream -- trusted_proxies is a reverse_proxy
        // concept only. Negative test guards against accidental field bleed.
        // Card #360.
        var routes = new List<RouteEntry>
        {
            new("docs", DefaultDomain("docs"), ServeMode.FileServer, Port: null, SpaFallback: false, ArtifactDirectory: "/srv/docs", Enabled: true)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var subrouteHandlers = GetRoutes(config)![1]!["handle"]![0]!["routes"]!.AsArray();
        var fileHandler = subrouteHandlers[1]!["handle"]![0]!;

        fileHandler["handler"]!.GetValue<string>().ShouldBe("file_server");
        fileHandler.AsObject().ContainsKey("trusted_proxies").ShouldBeFalse();
    }

    [Fact]
    public void Build_DisabledExternalRoute_EmitsServiceUnavailableStub()
    {
        var routes = new List<RouteEntry>
        {
            new
            (
                "crawl4ai",
                DefaultDomain("crawl4ai"),
                ServeMode.ReverseProxy,
                Port: null,
                SpaFallback: false,
                ArtifactDirectory: null,
                Enabled: false,
                ResponseHeaders: null,
                SecurityHeaders: null,
                ExternalDial: "192.168.1.50:11235",
                ExternalScheme: "http"
            )
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var handler = GetRoutes(config)![1]!["handle"]![0]!;
        handler["handler"]!.GetValue<string>().ShouldBe("static_response");
        handler["status_code"]!.GetValue<string>().ShouldBe("503");
    }

    // ----- Card #369: runtime-config-file writable-overlay emission -----

    [Fact]
    public void Build_FileServerRoute_NoRuntimeConfig_ShapeUnchanged()
    {
        // Migration-safe default: both runtime-config fields null -> the file-
        // server subroute is byte-identical to the pre-#369 shape (vars root +
        // file_server, count 2). No overlay branch emitted.
        var routes = new List<RouteEntry>
        {
            new("docs", DefaultDomain("docs"), ServeMode.FileServer, Port: null, SpaFallback: false, ArtifactDirectory: "/srv/docs", Enabled: true)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var subrouteHandlers = GetRoutes(config)![1]!["handle"]![0]!["routes"]!.AsArray();
        subrouteHandlers.Count.ShouldBe(2);
        subrouteHandlers[0]!["handle"]![0]!["handler"]!.GetValue<string>().ShouldBe("vars");
        subrouteHandlers[1]!["handle"]![0]!["handler"]!.GetValue<string>().ShouldBe("file_server");
    }

    [Fact]
    public void Build_FileServerRoute_OnlyOneRuntimeConfigFieldProvided_NoOverlayEmitted()
    {
        // Defensive: a half-populated pair (path without writable root, or vice
        // versa) must not emit a malformed overlay. Both-or-nothing.
        var pathOnly = new List<RouteEntry>
        {
            new("docs", DefaultDomain("docs"), ServeMode.FileServer, Port: null, SpaFallback: false, ArtifactDirectory: "/srv/docs", Enabled: true, RuntimeConfigFilePath: "/config.json", RuntimeConfigWritableRoot: null)
        };

        var rootOnly = new List<RouteEntry>
        {
            new("docs", DefaultDomain("docs"), ServeMode.FileServer, Port: null, SpaFallback: false, ArtifactDirectory: "/srv/docs", Enabled: true, RuntimeConfigFilePath: null, RuntimeConfigWritableRoot: "/var/lib/collabhost/data/app-data/docs")
        };

        foreach (var routes in new[] { pathOnly, rootOnly })
        {
            var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

            var subrouteHandlers = GetRoutes(config)![1]!["handle"]![0]!["routes"]!.AsArray();
            subrouteHandlers.Count.ShouldBe(2);
        }
    }

    [Fact]
    public void Build_FileServerRoute_WithRuntimeConfig_EmitsPathScopedOverlayRootedAtWritableDir()
    {
        var routes = new List<RouteEntry>
        {
            new
            (
                "portal",
                DefaultDomain("portal"),
                ServeMode.FileServer,
                Port: null,
                SpaFallback: false,
                ArtifactDirectory: "/srv/portal",
                Enabled: true,
                RuntimeConfigFilePath: "/config.json",
                RuntimeConfigWritableRoot: "/var/lib/collabhost/data/app-data/portal"
            )
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var subrouteHandlers = GetRoutes(config)![1]!["handle"]![0]!["routes"]!.AsArray();

        // 0 = blanket artifact vars root, 1 = overlay, 2 = file_server.
        subrouteHandlers.Count.ShouldBe(3);
        subrouteHandlers[0]!["handle"]![0]!["root"]!.GetValue<string>().ShouldBe("/srv/portal");

        var overlay = subrouteHandlers[1]!;

        // Overlay is path-scoped to the config path.
        overlay["match"]![0]!["path"]![0]!.GetValue<string>().ShouldBe("/config.json");

        // Overlay handle is a nested subroute whose first inner handler sets the
        // WRITABLE root -- the root switch is confined inside this branch.
        var innerHandlers = overlay["handle"]![0]!["routes"]!.AsArray();
        innerHandlers[0]!["handle"]![0]!["handler"]!.GetValue<string>().ShouldBe("vars");
        innerHandlers[0]!["handle"]![0]!["root"]!.GetValue<string>()
            .ShouldBe("/var/lib/collabhost/data/app-data/portal");

        // No-cache header rides inside the overlay branch (self-contained).
        innerHandlers[1]!["handle"]![0]!["handler"]!.GetValue<string>().ShouldBe("headers");
        innerHandlers[1]!["handle"]![0]!["response"]!["set"]!["Cache-Control"]![0]!
            .GetValue<string>().ShouldBe("no-cache");

        // file_server responder terminates the matched request inside the branch.
        innerHandlers[2]!["handle"]![0]!["handler"]!.GetValue<string>().ShouldBe("file_server");
    }

    [Fact]
    public void Build_FileServerRoute_RuntimeConfigWithSpaFallback_SpaFallbackStillSeesArtifactRoot()
    {
        // ROOT-BLEED REGRESSION GUARD (#369, HIGH). The overlay sets the writable
        // root, but that switch is confined to the overlay's path-matched nested
        // subroute. The SPA-fallback handler sits at the chain level where the
        // root is STILL the artifact root (set at index 0). If the overlay leaked
        // its root chain-wide, the SPA fallback would stat the writable dir, find
        // no /index.html, and deep-link routing would break on the exact app this
        // card fixes. We assert the root the SPA fallback SEES, not merely that
        // the handler is present.
        var routes = new List<RouteEntry>
        {
            new
            (
                "portal",
                DefaultDomain("portal"),
                ServeMode.FileServer,
                Port: null,
                SpaFallback: true,
                ArtifactDirectory: "/srv/portal",
                Enabled: true,
                RuntimeConfigFilePath: "/config.json",
                RuntimeConfigWritableRoot: "/var/lib/collabhost/data/app-data/portal"
            )
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings, _defaultHosting, _defaultPortal);

        var subrouteHandlers = GetRoutes(config)![1]!["handle"]![0]!["routes"]!.AsArray();

        // 0 = artifact vars root, 1 = overlay, 2 = SPA fallback, 3 = file_server.
        subrouteHandlers.Count.ShouldBe(4);

        // The chain-level root (index 0) is the ARTIFACT dir -- this is the root
        // every chain-level handler after the overlay sees, including the SPA
        // fallback. The overlay does NOT mutate it.
        var chainLevelRootHandler = subrouteHandlers[0]!["handle"]![0]!;
        chainLevelRootHandler["handler"]!.GetValue<string>().ShouldBe("vars");
        chainLevelRootHandler["root"]!.GetValue<string>().ShouldBe("/srv/portal");

        // The overlay (index 1) is a SELF-CONTAINED path-matched subroute -- its
        // writable root lives inside its nested routes, never at the chain level.
        var overlay = subrouteHandlers[1]!;
        overlay["match"]![0]!["path"]![0]!.GetValue<string>().ShouldBe("/config.json");
        overlay["handle"]![0]!["handler"]!.GetValue<string>().ShouldBe("subroute");

        // The SPA fallback (index 2) sits AFTER the overlay at the chain level.
        // Because the overlay never set a chain-level root, the root in scope for
        // this handler is still /srv/portal (the artifact dir at index 0).
        var spaFallback = subrouteHandlers[2]!;
        spaFallback["handle"]![0]!["handler"]!.GetValue<string>().ShouldBe("rewrite");
        var tryFiles = spaFallback["match"]![0]!["file"]!["try_files"]!.AsArray();
        tryFiles[1]!.GetValue<string>().ShouldBe("/index.html");

        // No chain-level handler from the overlay onward re-sets the root -- only
        // the overlay's NESTED subroute touches a root, scoped inside its path
        // match. Assert no chain-level sibling `vars` handler bled out.
        for (var i = 1; i < subrouteHandlers.Count; i++)
        {
            foreach (var inner in subrouteHandlers[i]!["handle"]!.AsArray())
            {
                var innerObject = inner!.AsObject();

                if (innerObject.TryGetPropertyValue("handler", out var handlerNode)
                    && string.Equals(handlerNode!.GetValue<string>(), "vars", StringComparison.Ordinal))
                {
                    Assert.Fail($"Chain-level vars handler at index {i.ToString(System.Globalization.CultureInfo.InvariantCulture)} -- root would bleed into the SPA fallback.");
                }
            }
        }

        subrouteHandlers[3]!["handle"]![0]!["handler"]!.GetValue<string>().ShouldBe("file_server");
    }
}
