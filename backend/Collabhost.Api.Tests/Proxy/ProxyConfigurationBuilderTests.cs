using System.Text.Json.Nodes;

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

        var hosting = new HostingSettings { ListenPort = 9000 };

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

    private static JsonArray? GetRoutes(JsonObject config) =>
        config["apps"]?["http"]?["servers"]?["srv0"]?["routes"]?.AsArray();
}
