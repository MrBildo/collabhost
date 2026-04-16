using System.Text.Json;
using System.Text.Json.Nodes;

using Collabhost.Api.Capabilities;
using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Registry;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Capabilities;

public class CapabilityResolverTests
{
    [Fact]
    public void ResolveJson_NoOverrides_ReturnsDefaults()
    {
        var defaults = """{"endpoint":"/health","intervalSeconds":30}""";

        var result = CapabilityResolver.ResolveJson(defaults, null);

        result.ShouldBe(defaults);
    }

    [Fact]
    public void ResolveJson_WithOverrides_MergesPrimitives()
    {
        var defaults = """{"endpoint":"/health","intervalSeconds":30,"timeoutSeconds":5}""";
        var overrides = """{"intervalSeconds":60}""";

        var result = CapabilityResolver.ResolveJson(defaults, overrides);

        var parsed = JsonNode.Parse(result)!.AsObject();

        parsed["endpoint"]!.GetValue<string>().ShouldBe("/health");
        parsed["intervalSeconds"]!.GetValue<int>().ShouldBe(60);
        parsed["timeoutSeconds"]!.GetValue<int>().ShouldBe(5);
    }

    [Fact]
    public void MergeJson_DictionaryProperty_MergesAtKeyLevel()
    {
        var defaults = """{"variables":{"NODE_ENV":"production","PORT":"3000"}}""";
        var overrides = """{"variables":{"LOG_LEVEL":"debug"}}""";

        var result = CapabilityResolver.MergeJson(defaults, overrides);

        var parsed = JsonNode.Parse(result)!.AsObject();
        var variables = parsed["variables"]!.AsObject();

        variables["NODE_ENV"]!.GetValue<string>().ShouldBe("production");
        variables["PORT"]!.GetValue<string>().ShouldBe("3000");
        variables["LOG_LEVEL"]!.GetValue<string>().ShouldBe("debug");
    }

    [Fact]
    public void MergeJson_DictionaryProperty_OverridesExistingKeys()
    {
        var defaults = """{"variables":{"NODE_ENV":"production"}}""";
        var overrides = """{"variables":{"NODE_ENV":"development"}}""";

        var result = CapabilityResolver.MergeJson(defaults, overrides);

        var parsed = JsonNode.Parse(result)!.AsObject();
        var variables = parsed["variables"]!.AsObject();

        variables["NODE_ENV"]!.GetValue<string>().ShouldBe("development");
    }

    [Fact]
    public void MergeJson_MixedProperties_HandlesBothPrimitivesAndDictionaries()
    {
        var defaults = """{"endpoint":"/health","variables":{"A":"1"}}""";
        var overrides = """{"endpoint":"/healthz","variables":{"B":"2"}}""";

        var result = CapabilityResolver.MergeJson(defaults, overrides);

        var parsed = JsonNode.Parse(result)!.AsObject();

        parsed["endpoint"]!.GetValue<string>().ShouldBe("/healthz");
        parsed["variables"]!.AsObject()["A"]!.GetValue<string>().ShouldBe("1");
        parsed["variables"]!.AsObject()["B"]!.GetValue<string>().ShouldBe("2");
    }

    [Fact]
    public void Resolve_DeserializesToTypedConfig()
    {
        var defaults = """{"endpoint":"/health","intervalSeconds":30,"timeoutSeconds":5}""";

        var result = CapabilityResolver.Resolve<HealthCheckConfiguration>(defaults, null);

        result.Endpoint.ShouldBe("/health");
        result.IntervalSeconds.ShouldBe(30);
        result.TimeoutSeconds.ShouldBe(5);
    }

    [Fact]
    public void Resolve_WithOverrides_AppliesMergeThenDeserializes()
    {
        var defaults = """{"endpoint":"/health","intervalSeconds":30,"timeoutSeconds":5}""";
        var overrides = """{"intervalSeconds":10}""";

        var result = CapabilityResolver.Resolve<HealthCheckConfiguration>(defaults, overrides);

        result.Endpoint.ShouldBe("/health");
        result.IntervalSeconds.ShouldBe(10);
        result.TimeoutSeconds.ShouldBe(5);
    }

    [Fact]
    public void Resolve_EnumValues_DeserializesCorrectly()
    {
        var defaults = """{"policy":"onCrash"}""";

        var result = CapabilityResolver.Resolve<RestartConfiguration>(defaults, null);

        result.Policy.ShouldBe(RestartPolicy.OnCrash);
    }

    [Fact]
    public void ValidateEdits_LockedField_ReturnsError()
    {
        var overrides = new JsonObject
        {
            ["domainPattern"] = "custom.example.com"
        };

        var errors = CapabilityResolver.ValidateEdits("routing", overrides, isNewApp: false);

        errors.Count.ShouldBe(1);
        errors[0].ShouldContain("domainPattern");
        errors[0].ShouldContain("Set during registration");
    }

    [Fact]
    public void ValidateEdits_EditableField_ReturnsNoError()
    {
        var overrides = new JsonObject
        {
            ["spaFallback"] = true
        };

        var errors = CapabilityResolver.ValidateEdits("routing", overrides, isNewApp: false);

        errors.Count.ShouldBe(0);
    }

    [Fact]
    public void ValidateEdits_UnknownField_ReturnsError()
    {
        var overrides = new JsonObject
        {
            ["nonExistentField"] = "value"
        };

        var errors = CapabilityResolver.ValidateEdits("routing", overrides, isNewApp: false);

        errors.Count.ShouldBe(1);
        errors[0].ShouldContain("Unknown field");
    }

    [Fact]
    public void ValidateEdits_LockedField_AllowedForNewApp()
    {
        var overrides = new JsonObject
        {
            ["domainPattern"] = "custom.example.com"
        };

        var errors = CapabilityResolver.ValidateEdits("routing", overrides, isNewApp: true);

        errors.Count.ShouldBe(0);
    }

    [Fact]
    public void ValidateEdits_LockedArtifactLocation_AllowedDuringRegistration()
    {
        var overrides = new JsonObject
        {
            ["location"] = "C:\\Projects\\my-site"
        };

        var errors = CapabilityResolver.ValidateEdits("artifact", overrides, isNewApp: true);

        errors.Count.ShouldBe(0);
    }

    [Fact]
    public void ValidateEdits_LockedArtifactLocation_RejectedAfterRegistration()
    {
        var overrides = new JsonObject
        {
            ["location"] = "C:\\Projects\\my-site"
        };

        var errors = CapabilityResolver.ValidateEdits("artifact", overrides, isNewApp: false);

        errors.Count.ShouldBe(1);
        errors[0].ShouldContain("location");
        errors[0].ShouldContain("Set during registration");
    }

    [Fact]
    public void ValidateEdits_DerivedField_AllowedForNewApp()
    {
        var overrides = new JsonObject
        {
            ["spaFallback"] = true
        };

        var errors = CapabilityResolver.ValidateEdits("routing", overrides, isNewApp: true);

        errors.Count.ShouldBe(0);
    }

    [Fact]
    public void ValidateEdits_UnknownCapability_ReturnsNoErrors()
    {
        var overrides = new JsonObject
        {
            ["anything"] = "value"
        };

        var errors = CapabilityResolver.ValidateEdits("unknown-capability", overrides, isNewApp: false);

        errors.Count.ShouldBe(0);
    }

    [Fact]
    public void MergeJson_InvalidDefaults_Throws() =>
        Should.Throw<JsonException>(
            () => CapabilityResolver.MergeJson("not-json", """{"key":"value"}""")
        );

    [Fact]
    public void MergeJson_InvalidOverrides_Throws() =>
        Should.Throw<JsonException>(
            () => CapabilityResolver.MergeJson("""{"key":"value"}""", "not-json")
        );

    [Theory]
    [InlineData("NODE_ENV")]
    [InlineData("_PRIVATE")]
    [InlineData("A")]
    [InlineData("myVar123")]
    [InlineData("__double_underscore")]
    public void ValidateEdits_ValidEnvironmentVariableKey_ReturnsNoError(string key)
    {
        var overrides = new JsonObject
        {
            ["variables"] = new JsonObject { [key] = "value" }
        };

        var errors = CapabilityResolver.ValidateEdits("environment-defaults", overrides, isNewApp: false);

        errors.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("MY VAR")]
    [InlineData("1STARTS_WITH_DIGIT")]
    [InlineData("key=value")]
    [InlineData("with\"quotes")]
    [InlineData("path/var")]
    [InlineData("has-dash")]
    [InlineData("has.dot")]
    public void ValidateEdits_InvalidEnvironmentVariableKey_ReturnsError(string key)
    {
        var overrides = new JsonObject
        {
            ["variables"] = new JsonObject { [key] = "value" }
        };

        var errors = CapabilityResolver.ValidateEdits("environment-defaults", overrides, isNewApp: false);

        errors.Count.ShouldBe(1);
        errors[0].ShouldContain("Invalid key");
        errors[0].ShouldContain(key);
    }

    [Fact]
    public void ValidateEdits_MultipleInvalidKeys_ReturnsMultipleErrors()
    {
        var overrides = new JsonObject
        {
            ["variables"] = new JsonObject
            {
                ["VALID_KEY"] = "ok",
                ["bad key"] = "not ok",
                ["123invalid"] = "not ok"
            }
        };

        var errors = CapabilityResolver.ValidateEdits("environment-defaults", overrides, isNewApp: false);

        errors.Count.ShouldBe(2);
    }

    [Fact]
    public void ValidateEdits_EnvironmentVariableKeys_ValidatedDuringRegistration()
    {
        var overrides = new JsonObject
        {
            ["variables"] = new JsonObject { ["bad key"] = "value" }
        };

        var errors = CapabilityResolver.ValidateEdits("environment-defaults", overrides, isNewApp: true);

        errors.Count.ShouldBe(1);
        errors[0].ShouldContain("Invalid key");
    }

    [Fact]
    public void ResolveDomain_ReplacesSlugAndBaseDomain()
    {
        var result = CapabilityResolver.ResolveDomain("{slug}.{baseDomain}", "my-app", "test.internal");

        result.ShouldBe("my-app.test.internal");
    }

    [Fact]
    public void ResolveDomain_CustomDomainWithoutTokens_PassesThrough()
    {
        var result = CapabilityResolver.ResolveDomain("custom.example.com", "my-app", "test.internal");

        result.ShouldBe("custom.example.com");
    }

    [Fact]
    public void ResolveDomain_SlugOnlyToken_ReplacesSlug()
    {
        var result = CapabilityResolver.ResolveDomain("{slug}.collab.internal", "my-app", "test.internal");

        result.ShouldBe("my-app.collab.internal");
    }
}
