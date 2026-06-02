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
    public void ValidateEdits_FixedPort_ValidValue_ReturnsNoError()
    {
        var overrides = new JsonObject
        {
            ["fixedPort"] = 8888
        };

        var errors = CapabilityResolver.ValidateEdits("port-injection", overrides, false);

        errors.Count.ShouldBe(0);
    }

    [Fact]
    public void ValidateEdits_FixedPort_Zero_ReturnsNoError()
    {
        // Zero is the "no pin" sentinel -- a legitimate value meaning automatic
        // allocation, not an out-of-range error.
        var overrides = new JsonObject
        {
            ["fixedPort"] = 0
        };

        var errors = CapabilityResolver.ValidateEdits("port-injection", overrides, false);

        errors.Count.ShouldBe(0);
    }

    [Fact]
    public void ValidateEdits_FixedPort_AboveMaximum_ReturnsError()
    {
        var overrides = new JsonObject
        {
            ["fixedPort"] = 70000
        };

        var errors = CapabilityResolver.ValidateEdits("port-injection", overrides, false);

        errors.Count.ShouldBe(1);
        errors[0].ShouldContain("fixedPort");
        errors[0].ShouldContain("less than or equal to 65535");
    }

    [Fact]
    public void ValidateEdits_FixedPort_Negative_ReturnsError()
    {
        var overrides = new JsonObject
        {
            ["fixedPort"] = -1
        };

        var errors = CapabilityResolver.ValidateEdits("port-injection", overrides, false);

        errors.Count.ShouldBe(1);
        errors[0].ShouldContain("fixedPort");
        errors[0].ShouldContain("greater than or equal to 0");
    }

    [Fact]
    public void Resolve_FixedPortOverride_FlowsThroughToConfiguration()
    {
        var defaults = """{"environmentVariableName":"PORT","portFormat":"{port}","fixedPort":0}""";
        var overrides = """{"fixedPort":8888}""";

        var resolved = CapabilityResolver.Resolve<PortInjectionConfiguration>(defaults, overrides);

        resolved.FixedPort.ShouldBe(8888);
        resolved.EnvironmentVariableName.ShouldBe("PORT");
    }

    [Fact]
    public void Resolve_NoFixedPort_DefaultsToZeroMeaningAutomaticAllocation()
    {
        var defaults = """{"environmentVariableName":"PORT","portFormat":"{port}"}""";

        var resolved = CapabilityResolver.Resolve<PortInjectionConfiguration>(defaults, null);

        resolved.FixedPort.ShouldBe(0);
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
    [InlineData("NODE_ENV\n")]   // #310: trailing newline rejected (\z anchor, not $)
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
    [InlineData("/config.json::Cache-Control\n")]    // #310: trailing newline rejected (\z anchor, not $)
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

    // Card #336: runtime-config-file keys are JSON identifiers and must NOT
    // fall through to the env-var pattern (which would reject hyphenated keys
    // like "api-base-url"). Marcus precondition #2.

    [Fact]
    public void ValidateEdits_RuntimeConfigFile_HyphenatedKey_Accepted()
    {
        var overrides = new JsonObject
        {
            ["values"] = new JsonObject { ["api-base-url"] = "https://api.example.com" }
        };

        var errors = CapabilityResolver.ValidateEdits("runtime-config-file", overrides, isNewApp: false);

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void ValidateEdits_RuntimeConfigFile_DotNotationKey_Accepted()
    {
        var overrides = new JsonObject
        {
            ["values"] = new JsonObject { ["app.config.url"] = "https://api.example.com" }
        };

        var errors = CapabilityResolver.ValidateEdits("runtime-config-file", overrides, isNewApp: false);

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void ValidateEdits_RuntimeConfigFile_WhitespaceKey_Rejected()
    {
        var overrides = new JsonObject
        {
            ["values"] = new JsonObject { ["api base url"] = "https://api.example.com" }
        };

        var errors = CapabilityResolver.ValidateEdits("runtime-config-file", overrides, isNewApp: false);

        errors.Count.ShouldBe(1);
        errors[0].ShouldContain("'api base url'");
    }

    [Fact]
    public void ValidateEdits_RuntimeConfigFile_EmptyKey_Rejected()
    {
        var overrides = new JsonObject
        {
            ["values"] = new JsonObject { [""] = "value" }
        };

        var errors = CapabilityResolver.ValidateEdits("runtime-config-file", overrides, isNewApp: false);

        errors.Count.ShouldBe(1);
    }

    [Fact]
    public void ValidateEdits_RuntimeConfigFile_TrailingNewlineKey_Rejected()
    {
        // #310 regression: in .NET, $ matches before a trailing \n, which
        // would admit a newline-bearing key (e.g. "api-base-url\n") that
        // lands downstream in the generated runtime-config JSON. The pattern
        // anchors with \z so the trailing newline is rejected.
        var overrides = new JsonObject
        {
            ["values"] = new JsonObject { ["api-base-url\n"] = "https://api.example.com" }
        };

        var errors = CapabilityResolver.ValidateEdits("runtime-config-file", overrides, isNewApp: false);

        errors.Count.ShouldBe(1);
        errors[0].ShouldContain("Invalid key");
    }

    // ----- Card #309: per-field key-pattern (security-headers.headers) -----

    [Theory]
    [InlineData("X-Content-Type-Options")]
    [InlineData("Strict-Transport-Security")]
    [InlineData("Referrer-Policy")]
    [InlineData("X-Frame-Options")]
    [InlineData("Cache-Control")]
    [InlineData("X-Custom-Header-1")]
    public void ValidateEdits_SecurityHeaders_ValidHeaderName_ReturnsNoError(string key)
    {
        var overrides = new JsonObject
        {
            ["headers"] = new JsonObject { [key] = "nosniff" }
        };

        var errors = CapabilityResolver.ValidateEdits("security-headers", overrides, isNewApp: false);

        errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]                          // empty
    [InlineData("Bad Header")]                // contains space
    [InlineData("Header:With:Colon")]         // contains colon
    [InlineData("Header\"WithQuote")]         // contains quote
    [InlineData("/path/scoped::Header-Name")] // compound shape rejected (use routing.responseHeaders)
    [InlineData("X-Content-Type-Options\n")]  // #310: trailing newline rejected (\z anchor, not $)
    public void ValidateEdits_SecurityHeaders_InvalidHeaderName_ReturnsError(string key)
    {
        var overrides = new JsonObject
        {
            ["headers"] = new JsonObject { [key] = "value" }
        };

        var errors = CapabilityResolver.ValidateEdits("security-headers", overrides, isNewApp: false);

        errors.Count.ShouldBe(1);
        errors[0].ShouldContain("Invalid key");
        // The operator-facing message is the security-headers message, not env-var.
        errors[0].ShouldContain("valid HTTP header name");
    }

    [Fact]
    public void ValidateEdits_SecurityHeaders_EnableHstsTrueAndStsInMap_Rejected()
    {
        // Cross-field collision: convenience flag AND freeform map BOTH carry
        // the HSTS channel. Bill ruling (precondition #3): ValidateEdits
        // rejects the cross-field state. The operator must choose one channel.
        var overrides = new JsonObject
        {
            ["enableHsts"] = true,
            ["headers"] = new JsonObject
            {
                ["Strict-Transport-Security"] = "max-age=63072000; includeSubDomains"
            }
        };

        var errors = CapabilityResolver.ValidateEdits("security-headers", overrides, isNewApp: false);

        errors.Count.ShouldBe(1);
        errors[0].ShouldContain("Strict-Transport-Security");
        errors[0].ShouldContain("Enable HSTS");
    }

    [Fact]
    public void ValidateEdits_SecurityHeaders_EnableHstsTrueAndMapEmpty_Accepted()
    {
        // The typed flag fires alone -- no collision. This is the standard
        // "operator opts into HSTS via the checkbox" state and must pass.
        var overrides = new JsonObject
        {
            ["enableHsts"] = true,
            ["headers"] = new JsonObject()
        };

        var errors = CapabilityResolver.ValidateEdits("security-headers", overrides, isNewApp: false);

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void ValidateEdits_SecurityHeaders_EnableHstsTrueAndUnrelatedHeader_Accepted()
    {
        // The cross-field rule fires only on the STS key. An operator who
        // turns on HSTS AND sets a Referrer-Policy in the map is doing two
        // independent things -- both legitimate.
        var overrides = new JsonObject
        {
            ["enableHsts"] = true,
            ["headers"] = new JsonObject
            {
                ["Referrer-Policy"] = "strict-origin-when-cross-origin"
            }
        };

        var errors = CapabilityResolver.ValidateEdits("security-headers", overrides, isNewApp: false);

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void ValidateEdits_SecurityHeaders_EnableHstsFalseAndStsInMap_Accepted()
    {
        // Without the convenience flag the map is the sole channel. Operator
        // who hand-authors STS gets to do so without the convenience flag
        // colliding.
        var overrides = new JsonObject
        {
            ["enableHsts"] = false,
            ["headers"] = new JsonObject
            {
                ["Strict-Transport-Security"] = "max-age=63072000; includeSubDomains; preload"
            }
        };

        var errors = CapabilityResolver.ValidateEdits("security-headers", overrides, isNewApp: false);

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void ValidateEdits_SecurityHeaders_OnlyStsInMap_Accepted()
    {
        // Map-only path with no enableHsts in the override at all (inherits
        // type default of false). No collision.
        var overrides = new JsonObject
        {
            ["headers"] = new JsonObject
            {
                ["Strict-Transport-Security"] = "max-age=300"
            }
        };

        var errors = CapabilityResolver.ValidateEdits("security-headers", overrides, isNewApp: false);

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void ValidateMergedOverrides_SecurityHeaders_HstsTrueAndStsInMap_Rejected()
    {
        // The two-step operator-edit collision path that the in-flight
        // ValidateEdits check alone CANNOT catch. The endpoint passes the
        // post-merge effective override (existing-stored ∪ in-flight delta)
        // and the cross-field check fires against the merged state. This is
        // the load-bearing contract behind precondition #3 -- the F1 finding
        // from Kai's PR #213 review.
        var merged = new JsonObject
        {
            ["enableHsts"] = true,
            ["headers"] = new JsonObject
            {
                ["Strict-Transport-Security"] = "max-age=300"
            }
        };

        var errors = CapabilityResolver.ValidateMergedOverrides("security-headers", merged);

        errors.Count.ShouldBe(1);
        errors[0].ShouldContain("Strict-Transport-Security");
        errors[0].ShouldContain("Enable HSTS");
    }

    [Fact]
    public void ValidateMergedOverrides_SecurityHeaders_HstsTrueAndMapEmpty_Accepted()
    {
        // Symmetric to the ValidateEdits_..._Accepted case but tested against
        // the merged-state entry point. Operator opts into HSTS via the typed
        // flag, map is empty -- no collision.
        var merged = new JsonObject
        {
            ["enableHsts"] = true,
            ["headers"] = new JsonObject()
        };

        var errors = CapabilityResolver.ValidateMergedOverrides("security-headers", merged);

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void ValidateMergedOverrides_SecurityHeaders_HstsFalseAndStsInMap_Accepted()
    {
        // Map-only HSTS authoring is the alternate channel. With the
        // convenience flag off, the operator's hand-authored STS row is the
        // sole emission channel -- no collision.
        var merged = new JsonObject
        {
            ["enableHsts"] = false,
            ["headers"] = new JsonObject
            {
                ["Strict-Transport-Security"] = "max-age=63072000"
            }
        };

        var errors = CapabilityResolver.ValidateMergedOverrides("security-headers", merged);

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void ValidateMergedOverrides_NonSecurityHeadersCapability_NoError()
    {
        // The cross-field check is security-headers-specific today. Other
        // capabilities flowing through ValidateMergedOverrides return clean.
        var merged = new JsonObject
        {
            ["enableHsts"] = true,
            ["headers"] = new JsonObject
            {
                ["Strict-Transport-Security"] = "max-age=300"
            }
        };

        var errors = CapabilityResolver.ValidateMergedOverrides("routing", merged);

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void Resolve_SecurityHeaders_TypeDefaultsCarryXctoSeed()
    {
        // The Rule 3 disclosure case: every routed app type's type-level
        // default ships the XCTO seed. When no override is present, the
        // resolved spec carries the seed verbatim and emission produces the
        // nosniff header. Migration test pair, precondition #11.
        var defaults = """
            {"enableHsts":false,"hstsMaxAgeSeconds":300,"headers":{"X-Content-Type-Options":"nosniff"}}
            """;

        var resolved = CapabilityResolver.Resolve<SecurityHeadersConfiguration>(defaults, null);

        resolved.EnableHsts.ShouldBeFalse();
        resolved.HstsMaxAgeSeconds.ShouldBe(300);
        resolved.Headers.ShouldContainKey("X-Content-Type-Options");
        resolved.Headers["X-Content-Type-Options"].ShouldBe("nosniff");
    }

    [Fact]
    public void Resolve_SecurityHeaders_OverrideWithEmptyMap_DefaultsSurvive()
    {
        // The documented MergeJson semantic, surfaced for operator awareness
        // (precondition #11 migration pair): an override row that carries
        // headers: {} does NOT delete the type-default entries because
        // MergeJson is one-level-deep and shallow-merges JsonObject keys. The
        // operator-facing suppression path is per-entry override-to-empty,
        // dropped at emission -- NOT clearing the map.
        var defaults = """
            {"enableHsts":false,"hstsMaxAgeSeconds":300,"headers":{"X-Content-Type-Options":"nosniff"}}
            """;
        var overrides = """{"headers":{}}""";

        var resolved = CapabilityResolver.Resolve<SecurityHeadersConfiguration>(defaults, overrides);

        // XCTO seed still present -- clearing the map did NOT remove it.
        resolved.Headers.ShouldContainKey("X-Content-Type-Options");
        resolved.Headers["X-Content-Type-Options"].ShouldBe("nosniff");
    }

    [Fact]
    public void Resolve_SecurityHeaders_OverrideXctoToEmptyString_PreservedInResolvedSpec()
    {
        // Operator-suppression channel: override row carries the XCTO key
        // with value = "" (the emission helper drops empty-valued entries at
        // build time). MergeJson shallow-merges, so the resolved spec carries
        // the empty value, NOT the original "nosniff". Builder is responsible
        // for the drop; resolver carries the operator intent.
        var defaults = """
            {"enableHsts":false,"hstsMaxAgeSeconds":300,"headers":{"X-Content-Type-Options":"nosniff"}}
            """;
        var overrides = """{"headers":{"X-Content-Type-Options":""}}""";

        var resolved = CapabilityResolver.Resolve<SecurityHeadersConfiguration>(defaults, overrides);

        resolved.Headers["X-Content-Type-Options"].ShouldBe("");
    }

    [Fact]
    public void Resolve_SecurityHeaders_NullHeaders_NormalizedToEmpty()
    {
        // Same null-normalization shape as RoutingConfiguration.ResponseHeaders
        // and RuntimeConfigFileConfiguration.Values. An override {"headers":null}
        // passes ValidateEdits (null is not a JsonObject) and flows through
        // MergeJson's else-branch overwriting defaults["headers"] with null.
        // The explicit setter normalizes to an empty dictionary so the emitter
        // never sees a null reference.
        var defaults = """
            {"enableHsts":false,"hstsMaxAgeSeconds":300,"headers":{"X-Content-Type-Options":"nosniff"}}
            """;
        var overrideWithNull = """{"headers":null}""";

        var resolved = CapabilityResolver.Resolve<SecurityHeadersConfiguration>(defaults, overrideWithNull);

        resolved.Headers.ShouldNotBeNull();
        resolved.Headers.Count.ShouldBe(0);
    }
}
