using System.Text.Json.Nodes;

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
        SelfPort = 58400,
        AdminPort = 2019
    };

    [Fact]
    public void Build_EmptyRoutes_ReturnsSelfRouteOnly()
    {
        var config = ProxyConfigurationBuilder.Build([], _defaultSettings);

        var routes = GetRoutes(config);

        routes.ShouldNotBeNull();
        routes.Count.ShouldBe(1);
        routes[0]!["@id"]!.GetValue<string>().ShouldBe("route_collabhost");
    }

    [Fact]
    public void Build_SelfRoute_HasFlushInterval()
    {
        var config = ProxyConfigurationBuilder.Build([], _defaultSettings);

        var selfRoute = GetRoutes(config)![0]!;

        var handler = selfRoute["handle"]![0]!;

        handler["flush_interval"]!.GetValue<int>().ShouldBe(-1);
    }

    [Fact]
    public void Build_SelfRoute_HasCorrectHostAndDial()
    {
        var config = ProxyConfigurationBuilder.Build([], _defaultSettings);

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
            new("my-app", ServeMode.ReverseProxy, Port: 5000, SpaFallback: false, ArtifactDirectory: null, Enabled: true)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings);

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
            new("my-app", ServeMode.ReverseProxy, Port: null, SpaFallback: false, ArtifactDirectory: null, Enabled: true)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings);

        var appRoute = GetRoutes(config)![1]!;
        var dial = appRoute["handle"]![0]!["upstreams"]![0]!["dial"]!.GetValue<string>();
        dial.ShouldBe("localhost:0");
    }

    [Fact]
    public void Build_FileServerRoute_HasSubrouteWithFileServer()
    {
        var routes = new List<RouteEntry>
        {
            new("docs", ServeMode.FileServer, Port: null, SpaFallback: false, ArtifactDirectory: "/srv/docs", Enabled: true)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings);

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
            new("spa", ServeMode.FileServer, Port: null, SpaFallback: true, ArtifactDirectory: "/srv/spa", Enabled: true)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings);

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
            new("offline", ServeMode.ReverseProxy, Port: 5000, SpaFallback: false, ArtifactDirectory: null, Enabled: false)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings);

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
            new("app-a", ServeMode.ReverseProxy, Port: 3000, SpaFallback: false, ArtifactDirectory: null, Enabled: true),
            new("app-b", ServeMode.FileServer, Port: null, SpaFallback: false, ArtifactDirectory: "/srv/b", Enabled: true)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings);

        var subjects = config["apps"]!["tls"]!["automation"]!["policies"]![0]!["subjects"]!.AsArray();
        subjects.Count.ShouldBe(3);
        subjects[0]!.GetValue<string>().ShouldBe("collabhost.collab.internal");
        subjects[1]!.GetValue<string>().ShouldBe("app-a.collab.internal");
        subjects[2]!.GetValue<string>().ShouldBe("app-b.collab.internal");
    }

    [Fact]
    public void Build_PkiConfig_HasLocalAuthority()
    {
        var config = ProxyConfigurationBuilder.Build([], _defaultSettings);

        var pki = config["apps"]!["pki"]!;
        var ca = pki["certificate_authorities"]!["local"]!;
        ca["name"]!.GetValue<string>().ShouldBe("Collabhost Local Authority");
        ca["install_trust"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public void Build_HttpConfig_UsesSettingsListenAddress()
    {
        var config = ProxyConfigurationBuilder.Build([], _defaultSettings);

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
            SelfPort = 58400,
            AdminPort = 9876
        };

        var config = ProxyConfigurationBuilder.Build([], settings);

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
            SelfPort = 9000,
            AdminPort = 2019
        };

        var routes = new List<RouteEntry>
        {
            new("test-app", ServeMode.ReverseProxy, Port: 8080, SpaFallback: false, ArtifactDirectory: null, Enabled: true)
        };

        var config = ProxyConfigurationBuilder.Build(routes, settings);

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
            new("alpha", ServeMode.ReverseProxy, Port: 3000, SpaFallback: false, ArtifactDirectory: null, Enabled: true),
            new("bravo", ServeMode.ReverseProxy, Port: 3001, SpaFallback: false, ArtifactDirectory: null, Enabled: true),
            new("charlie", ServeMode.FileServer, Port: null, SpaFallback: true, ArtifactDirectory: "/srv/c", Enabled: true)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings);

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
            new("static-site", ServeMode.FileServer, Port: null, SpaFallback: true, ArtifactDirectory: "/srv/site", Enabled: false)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings);

        var appRoute = GetRoutes(config)![1]!;
        appRoute["@id"]!.GetValue<string>().ShouldBe("route_static-site");

        var handler = appRoute["handle"]![0]!;
        handler["handler"]!.GetValue<string>().ShouldBe("static_response");
        handler["status_code"]!.GetValue<string>().ShouldBe("503");
    }

    [Fact]
    public void Build_CertLifetime_ReflectedInTlsConfig()
    {
        var settings = new ProxySettings
        {
            BaseDomain = "collab.internal",
            BinaryPath = "caddy",
            ListenAddress = ":443",
            CertLifetime = "720h",
            SelfPort = 58400,
            AdminPort = 2019
        };

        var config = ProxyConfigurationBuilder.Build([], settings);

        var lifetime = config["apps"]!["tls"]!["automation"]!["policies"]![0]!["issuers"]![0]!["lifetime"]!.GetValue<string>();
        lifetime.ShouldBe("720h");
    }

    [Fact]
    public void Build_AllRoutesTerminal()
    {
        var routes = new List<RouteEntry>
        {
            new("test", ServeMode.ReverseProxy, Port: 5000, SpaFallback: false, ArtifactDirectory: null, Enabled: true)
        };

        var config = ProxyConfigurationBuilder.Build(routes, _defaultSettings);

        var caddyRoutes = GetRoutes(config)!;

        foreach (var route in caddyRoutes)
        {
            route!["terminal"]!.GetValue<bool>().ShouldBeTrue();
        }
    }

    private static JsonArray? GetRoutes(JsonObject config) =>
        config["apps"]?["http"]?["servers"]?["srv0"]?["routes"]?.AsArray();
}
