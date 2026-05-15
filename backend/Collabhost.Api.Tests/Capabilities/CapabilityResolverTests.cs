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

        var errors = CapabilityResolver.ValidateEdits("routing", overrides, false);

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

        var errors = CapabilityResolver.ValidateEdits("routing", overrides, false);

        errors.Count.ShouldBe(0);
    }

    [Fact]
    public void ValidateEdits_UnknownField_ReturnsError()
    {
        var overrides = new JsonObject
        {
            ["nonExistentField"] = "value"
        };

        var errors = CapabilityResolver.ValidateEdits("routing", overrides, false);

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

        var errors = CapabilityResolver.ValidateEdits("routing", overrides, true);

        errors.Count.ShouldBe(0);
    }

    [Fact]
    public void ValidateEdits_LockedArtifactLocation_AllowedDuringRegistration()
    {
        var overrides = new JsonObject
        {
            ["location"] = "C:\\Projects\\my-site"
        };

        var errors = CapabilityResolver.ValidateEdits("artifact", overrides, true);

        errors.Count.ShouldBe(0);
    }

    [Fact]
    public void ValidateEdits_LockedArtifactLocation_RejectedAfterRegistration()
    {
        var overrides = new JsonObject
        {
            ["location"] = "C:\\Projects\\my-site"
        };

        var errors = CapabilityResolver.ValidateEdits("artifact", overrides, false);

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

        var errors = CapabilityResolver.ValidateEdits("routing", overrides, true);

        errors.Count.ShouldBe(0);
    }

    [Fact]
    public void ValidateEdits_UnknownCapability_ReturnsNoErrors()
    {
        var overrides = new JsonObject
        {
            ["anything"] = "value"
        };

        var errors = CapabilityResolver.ValidateEdits("unknown-capability", overrides, false);

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

        var errors = CapabilityResolver.ValidateEdits("environment-defaults", overrides, false);

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

        var errors = CapabilityResolver.ValidateEdits("environment-defaults", overrides, false);

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

        var errors = CapabilityResolver.ValidateEdits("environment-defaults", overrides, false);

        errors.Count.ShouldBe(2);
    }

    [Fact]
    public void ValidateEdits_EnvironmentVariableKeys_ValidatedDuringRegistration()
    {
        var overrides = new JsonObject
        {
            ["variables"] = new JsonObject { ["bad key"] = "value" }
        };

        var errors = CapabilityResolver.ValidateEdits("environment-defaults", overrides, true);

        errors.Count.ShouldBe(1);
        errors[0].ShouldContain("Invalid key");
    }

    // ----- Card #308: per-field key-pattern (responseHeaders) -----

    [Theory]
    [InlineData("/config.json::Cache-Control")]
    [InlineData("/index.html::X-Frame-Options")]
    [InlineData("/a/b/c.json::Cache-Control")]
    [InlineData("/config.json::X-Custom.Header_1")]
    [InlineData("/file::ETag")]
    public void ValidateEdits_ValidResponseHeaderKey_ReturnsNoError(string key)
    {
        var overrides = new JsonObject
        {
            ["responseHeaders"] = new JsonObject { [key] = "no-cache" }
        };

        var errors = CapabilityResolver.ValidateEdits("routing", overrides, false);

        errors.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData("config.json::Cache-Control")]      // no leading slash
    [InlineData("/config.json:Cache-Control")]       // single colon, not "::"
    [InlineData("/config.json::Cache Control")]      // space in header name
    [InlineData("/config.json::")]                   // empty header name
    [InlineData("::Cache-Control")]                  // empty path
    [InlineData("/with:colon::Cache-Control")]       // colon in path
    [InlineData("/config.json::Cache\"Control")]     // invalid header char
    [InlineData("Cache-Control")]                    // env-var-shaped key rejected here
    public void ValidateEdits_InvalidResponseHeaderKey_ReturnsError(string key)
    {
        var overrides = new JsonObject
        {
            ["responseHeaders"] = new JsonObject { [key] = "no-cache" }
        };

        var errors = CapabilityResolver.ValidateEdits("routing", overrides, false);

        errors.Count.ShouldBe(1);
        errors[0].ShouldContain("Invalid key");
        errors[0].ShouldContain(key);
        // The operator-facing message is the responseHeaders message, not the env-var one.
        errors[0].ShouldContain("<path>::<HeaderName>");
    }

    [Fact]
    public void ValidateEdits_ResponseHeaderKeys_ValidatedDuringRegistration()
    {
        var overrides = new JsonObject
        {
            ["responseHeaders"] = new JsonObject { ["bad-key"] = "no-cache" }
        };

        var errors = CapabilityResolver.ValidateEdits("routing", overrides, true);

        errors.Count.ShouldBe(1);
        errors[0].ShouldContain("Invalid key");
    }

    [Fact]
    public void ValidateEdits_EnvironmentVariableField_BehaviorUnchangedByKeyPatternAddition()
    {
        // Regression: the env-var field declares no KeyPattern, so it must keep
        // the exact pre-#308 POSIX-identifier contract and message. A
        // header-shaped key (valid for responseHeaders) must still be rejected
        // for env vars; a valid env key must still pass.
        var headerShaped = new JsonObject
        {
            ["variables"] = new JsonObject { ["/config.json::Cache-Control"] = "no-cache" }
        };

        var headerErrors = CapabilityResolver.ValidateEdits("environment-defaults", headerShaped, false);

        headerErrors.Count.ShouldBe(1);
        headerErrors[0].ShouldContain("Invalid key");
        headerErrors[0].ShouldContain("start with a letter or underscore");

        var validEnv = new JsonObject
        {
            ["variables"] = new JsonObject { ["NODE_ENV"] = "production" }
        };

        CapabilityResolver.ValidateEdits("environment-defaults", validEnv, false).Count.ShouldBe(0);
    }

    [Fact]
    public void MergeJson_ResponseHeadersFlatMap_MergesAtKeyLevelLikeEnvVars()
    {
        // §6.4 merge-safety: the flattened responseHeaders map merges
        // identically to the env-var map -- override keys add/replace, default
        // keys survive. This is why the flattened shape needs no new merge
        // semantics.
        var defaults = """{"responseHeaders":{"/config.json::Cache-Control":"no-cache"}}""";
        var overrides = """{"responseHeaders":{"/manifest.json::Cache-Control":"max-age=3600"}}""";

        var result = CapabilityResolver.MergeJson(defaults, overrides);

        var merged = JsonNode.Parse(result)!.AsObject()["responseHeaders"]!.AsObject();

        merged["/config.json::Cache-Control"]!.GetValue<string>().ShouldBe("no-cache");
        merged["/manifest.json::Cache-Control"]!.GetValue<string>().ShouldBe("max-age=3600");
    }

    [Fact]
    public void MergeJson_ResponseHeadersOverride_ReplacesSameKey()
    {
        var defaults = """{"responseHeaders":{"/config.json::Cache-Control":"no-cache"}}""";
        var overrides = """{"responseHeaders":{"/config.json::Cache-Control":"no-store"}}""";

        var result = CapabilityResolver.MergeJson(defaults, overrides);

        var merged = JsonNode.Parse(result)!.AsObject()["responseHeaders"]!.AsObject();

        merged["/config.json::Cache-Control"]!.GetValue<string>().ShouldBe("no-store");
    }

    [Fact]
    public void Resolve_RoutingWithSeededResponseHeaders_DeserializesDefault()
    {
        // An existing v1.0.x app whose stored routing config lacks
        // responseHeaders still deserializes; the new default arrives from the
        // (updated) default JSON via the existing additive-property path.
        var defaults = """
            {"domainPattern":"{slug}.{baseDomain}","serveMode":"FileServer","spaFallback":false,"responseHeaders":{"/config.json::Cache-Control":"no-cache"}}
            """;
        var legacyOverride = """{"spaFallback":true}""";

        var resolved = CapabilityResolver.Resolve<RoutingConfiguration>(defaults, legacyOverride);

        resolved.SpaFallback.ShouldBeTrue();
        resolved.ResponseHeaders.ShouldContainKey("/config.json::Cache-Control");
        resolved.ResponseHeaders["/config.json::Cache-Control"].ShouldBe("no-cache");
    }

    [Fact]
    public void Resolve_RoutingWithoutResponseHeaders_LeavesEmptyMap()
    {
        // The pre-#308 default JSON (no responseHeaders key) deserializes with
        // the C# default empty map -- no deserialization break.
        var defaults = """{"domainPattern":"{slug}.{baseDomain}","serveMode":"FileServer","spaFallback":false}""";

        var resolved = CapabilityResolver.Resolve<RoutingConfiguration>(defaults, null);

        resolved.ResponseHeaders.ShouldNotBeNull();
        resolved.ResponseHeaders.Count.ShouldBe(0);
    }

    [Fact]
    public void Resolve_RoutingWithExplicitNullResponseHeaders_NormalizesToEmpty()
    {
        // Regression guard for the NRE introduced by Card #308.
        //
        // An operator-crafted PUT /api/v1/apps/{slug}/settings with body
        // {"routing":{"responseHeaders":null}} passes ValidateEdits (JSON null
        // is not a JsonObject, so the KeyValue guard does not fire), flows
        // through MergeJson's else-branch writing defaults["responseHeaders"]=null,
        // and STJ deserializes the explicit null over the POCO initializer.
        //
        // Without the null-normalizing setter, ResponseHeaders would be null and
        // ProxyManager.LoadRoutableAppsAsync would NRE on .Count, breaking proxy
        // sync for every app in that pass.
        //
        // The fix: RoutingConfiguration.ResponseHeaders setter normalizes null
        // to an empty Dictionary at the resolve boundary so no consumer can
        // receive a null reference regardless of the deserialized JSON.
        var defaults = """{"domainPattern":"{slug}.{baseDomain}","serveMode":"FileServer","spaFallback":false,"responseHeaders":{"/config.json::Cache-Control":"no-cache"}}""";
        var overrideWithNull = """{"responseHeaders":null}""";

        var resolved = CapabilityResolver.Resolve<RoutingConfiguration>(defaults, overrideWithNull);

        // Must not throw -- the old code would NRE here.
        resolved.ResponseHeaders.ShouldNotBeNull();

        // Explicit null is normalized to empty (not the seed default -- the
        // override explicitly cleared it). Byte-identical pre-#308 subroute
        // shape is the downstream consequence: ProxyManager will produce null
        // responseHeaders → BuildFileServerRoute emits vars+file_server only.
        resolved.ResponseHeaders.Count.ShouldBe(0);
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
