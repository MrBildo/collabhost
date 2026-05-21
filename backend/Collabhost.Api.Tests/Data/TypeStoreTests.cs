using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Events;
using Collabhost.Api.Proxy;
using Collabhost.Api.Tests.Fixtures;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Data;

public class TypeStoreTests
{
    private static readonly ProxySettings _defaultProxySettings = new()
    {
        BaseDomain = "collab.internal",
        BinaryPath = "caddy",
        ListenAddress = ":443",
        CertLifetime = "168h"
    };

    private static TypeStore CreateTypeStore(string? baseDomain = null)
    {
        var proxySettings = baseDomain is null
            ? _defaultProxySettings
            : new ProxySettings
            {
                BaseDomain = baseDomain,
                BinaryPath = "caddy",
                ListenAddress = ":443",
                CertLifetime = "168h"
            };

        return new
        (
            new EventBus<TypeStoreReloadedEvent>(),
            new TypeStoreSettings { UserTypesDirectory = Path.Combine(Path.GetTempPath(), "collabhost-test-usertypes-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture)) },
            proxySettings,
            new StubHostEnvironment(),
            NullLogger<TypeStore>.Instance
        );
    }

    [Fact]
    public async Task LoadAsync_LoadsAllFiveBuiltInTypes()
    {
        var store = CreateTypeStore();

        await store.LoadAsync();

        store.ListTypes().Count.ShouldBe(5);
    }

    [Theory]
    [InlineData("dotnet-app")]
    [InlineData("nodejs-app")]
    [InlineData("static-site")]
    [InlineData("system-service")]
    [InlineData("executable")]
    public async Task LoadAsync_LoadsExpectedSlugs(string slug)
    {
        var store = CreateTypeStore();

        await store.LoadAsync();

        var type = store.GetBySlug(slug);

        type.ShouldNotBeNull();
        type.Slug.ShouldBe(slug);
    }

    [Fact]
    public async Task GetBySlug_ReturnsNullForUnknownSlug()
    {
        var store = CreateTypeStore();

        await store.LoadAsync();

        store.GetBySlug("nonexistent").ShouldBeNull();
    }

    [Fact]
    public async Task LoadAsync_DotNetApp_HasCorrectDisplayName()
    {
        var store = CreateTypeStore();

        await store.LoadAsync();

        var type = store.GetBySlug("dotnet-app");

        type.ShouldNotBeNull();
        type.DisplayName.ShouldBe(".NET Application");
    }

    [Fact]
    public async Task LoadAsync_AllTypesAreBuiltIn()
    {
        var store = CreateTypeStore();

        await store.LoadAsync();

        store.ListTypes().ShouldAllBe(type => type.IsBuiltIn);
    }

    [Theory]
    [InlineData("dotnet-app", false)]
    [InlineData("nodejs-app", false)]
    [InlineData("static-site", false)]
    [InlineData("executable", false)]
    [InlineData("system-service", true)]
    public async Task LoadAsync_SetsIsInternalFromJson(string slug, bool expected)
    {
        var store = CreateTypeStore();

        await store.LoadAsync();

        var type = store.GetBySlug(slug);

        type.ShouldNotBeNull();
        type.IsInternal.ShouldBe(expected);
    }

    [Fact]
    public async Task GetBySlug_ResolvesInternalTypes()
    {
        // ProxyAppSeeder relies on this -- internal types are loaded into the
        // store and resolvable by slug, even though they are filtered from
        // operator-facing list endpoints.
        var store = CreateTypeStore();

        await store.LoadAsync();

        store.GetBySlug("system-service").ShouldNotBeNull();
    }

    [Theory]
    [InlineData("dotnet-app", 9)]
    [InlineData("nodejs-app", 9)]
    [InlineData("static-site", 4)]
    [InlineData("system-service", 5)]
    [InlineData("executable", 8)]
    public async Task GetBindings_ReturnsCorrectBindingCount(string slug, int expectedCount)
    {
        var store = CreateTypeStore();

        await store.LoadAsync();

        var bindings = store.GetBindings(slug);

        bindings.ShouldNotBeNull();
        bindings.Count.ShouldBe(expectedCount);
    }

    [Fact]
    public async Task GetBindings_ReturnsNullForUnknownSlug()
    {
        var store = CreateTypeStore();

        await store.LoadAsync();

        store.GetBindings("nonexistent").ShouldBeNull();
    }

    [Theory]
    [InlineData("dotnet-app", "process", true)]
    [InlineData("dotnet-app", "port-injection", true)]
    [InlineData("dotnet-app", "routing", true)]
    [InlineData("dotnet-app", "health-check", true)]
    [InlineData("dotnet-app", "environment-defaults", true)]
    [InlineData("dotnet-app", "restart", true)]
    [InlineData("dotnet-app", "auto-start", true)]
    [InlineData("dotnet-app", "artifact", true)]
    [InlineData("static-site", "process", false)]
    [InlineData("static-site", "artifact", true)]
    [InlineData("static-site", "routing", true)]
    [InlineData("system-service", "routing", false)]
    [InlineData("system-service", "port-injection", false)]
    [InlineData("system-service", "security-headers", false)]
    [InlineData("executable", "health-check", false)]
    [InlineData("dotnet-app", "security-headers", true)]
    [InlineData("nodejs-app", "security-headers", true)]
    [InlineData("static-site", "security-headers", true)]
    [InlineData("executable", "security-headers", true)]
    [InlineData("nonexistent", "process", false)]
    public async Task HasBinding_ReturnsExpectedResult
    (
        string typeSlug,
        string capabilitySlug,
        bool expected
    )
    {
        var store = CreateTypeStore();

        await store.LoadAsync();

        store.HasBinding(typeSlug, capabilitySlug).ShouldBe(expected);
    }

    [Fact]
    public async Task GetBindings_DotNetApp_ProcessBinding_ContainsDiscoveryStrategy()
    {
        var store = CreateTypeStore();

        await store.LoadAsync();

        var bindings = store.GetBindings("dotnet-app");

        bindings.ShouldNotBeNull();
        bindings.ShouldContainKey("process");

        var processJson = bindings["process"];
        processJson.ShouldContain("DotNetRuntimeConfiguration");
    }

    [Fact]
    public async Task GetBindings_DotNetApp_EnvironmentDefaults_ContainsExpectedVars()
    {
        var store = CreateTypeStore();

        await store.LoadAsync();

        var bindings = store.GetBindings("dotnet-app");

        bindings.ShouldNotBeNull();
        bindings.ShouldContainKey("environment-defaults");

        var envJson = bindings["environment-defaults"];
        envJson.ShouldContain("ASPNETCORE_ENVIRONMENT");
        envJson.ShouldContain("DOTNET_ENVIRONMENT");
        envJson.ShouldContain("DOTNET_NOLOGO");
        envJson.ShouldContain("DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION");
    }

    [Theory]
    [InlineData("dotnet-app")]
    [InlineData("nodejs-app")]
    [InlineData("static-site")]
    [InlineData("executable")]
    public async Task LoadAsync_RoutingBinding_DomainPattern_ContainsConfiguredBaseDomain(string appTypeSlug)
    {
        var store = CreateTypeStore(baseDomain: "test.internal");

        await store.LoadAsync();

        var bindings = store.GetBindings(appTypeSlug);

        bindings.ShouldNotBeNull();
        bindings.ShouldContainKey("routing");

        var routingJson = bindings["routing"];

        routingJson.ShouldContain("test.internal");
        routingJson.ShouldNotContain("collab.internal");
    }

    // Card #309 precondition #10 -- defense-in-depth assertion: every app
    // type with a routing binding MUST also have a security-headers binding.
    // The pair travels together (security-headers is the response-header
    // analogue of routing). Catches the next-routed-app-type-added regression
    // where a new type's JSON forgets the security-headers binding and
    // silently loses the platform's XCTO default for that type.
    [Fact]
    public async Task LoadAsync_AllRoutedTypes_AlsoHaveSecurityHeadersBinding()
    {
        var store = CreateTypeStore();

        await store.LoadAsync();

        var routedWithoutSecurityHeaders = new List<string>();

        foreach (var type in store.ListTypes())
        {
            if (!store.HasBinding(type.Slug, "routing"))
            {
                continue;
            }

            if (!store.HasBinding(type.Slug, "security-headers"))
            {
                routedWithoutSecurityHeaders.Add(type.Slug);
            }
        }

        routedWithoutSecurityHeaders.ShouldBeEmpty
        (
            "Every app type with a `routing` binding must also have a "
            + "`security-headers` binding -- silent loss of the platform's "
            + "XCTO default is the regression this assertion guards. Card #309."
        );
    }
}
