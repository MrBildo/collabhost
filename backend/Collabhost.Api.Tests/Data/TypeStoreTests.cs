using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Events;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Data;

public class TypeStoreTests
{
    private static TypeStore CreateTypeStore() =>
        new
        (
            new EventBus<TypeStoreReloadedEvent>(),
            new TypeStoreSettings { UserTypesDirectory = Path.Combine(Path.GetTempPath(), "collabhost-test-usertypes-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture)) },
            NullLogger<TypeStore>.Instance
        );

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
    [InlineData("dotnet-app", 8)]
    [InlineData("nodejs-app", 8)]
    [InlineData("static-site", 2)]
    [InlineData("system-service", 4)]
    [InlineData("executable", 6)]
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
    [InlineData("executable", "health-check", false)]
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
}
