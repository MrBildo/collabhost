using System.Text.Json.Nodes;

using Collabhost.Api.Capabilities;
using Collabhost.Api.Capabilities.Configurations;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Capabilities;

// Card #348 schema + validation tests for the external-target capability.
// Schema-level: default values, Required-on-host/port, MinValue/MaxValue on
// port. Validation: host-pattern accept/reject matrix, AllowPublicHosts
// opt-in semantics, wire-vs-compile regex discipline (per S58 #310 LESSON).
public class ExternalTargetConfigurationTests
{
    [Fact]
    public void DefaultValues_AreEmptyHostZeroPortHttpScheme()
    {
        var configuration = new ExternalTargetConfiguration();

        configuration.Host.ShouldBe("");
        configuration.Port.ShouldBe(0);
        configuration.Scheme.ShouldBe("http");
    }

    [Fact]
    public void Schema_DeclaresHostPortSchemeFields()
    {
        var schema = ExternalTargetConfiguration.Schema;

        schema.Count.ShouldBe(3);
        schema[0].Key.ShouldBe("host");
        schema[0].Required.ShouldBeTrue();
        schema[0].ValuePattern.ShouldBe(CapabilityResolver.ExternalTargetHostPatternString);
        schema[1].Key.ShouldBe("port");
        schema[1].Required.ShouldBeTrue();
        schema[1].MinValue.ShouldBe(1);
        schema[1].MaxValue.ShouldBe(65535);
        schema[2].Key.ShouldBe("scheme");
        schema[2].Options!.Count.ShouldBe(2);
    }

    // ---- Required-field guard ----

    [Fact]
    public void ValidateEdits_EmptyHostAtRegistration_ReturnsError()
    {
        var overrides = new JsonObject
        {
            ["host"] = "",
            ["port"] = 8080
        };

        var errors = CapabilityResolver.ValidateEdits("external-target", overrides, isNewApp: true);

        errors.Count.ShouldBe(1);
        errors[0].ShouldContain("host");
        errors[0].ShouldContain("required");
    }

    [Fact]
    public void ValidateEdits_ZeroPortAtRegistration_ReturnsError()
    {
        var overrides = new JsonObject
        {
            ["host"] = "localhost",
            ["port"] = 0
        };

        var errors = CapabilityResolver.ValidateEdits("external-target", overrides, isNewApp: true);

        // Two errors: Required (port == 0 reads as empty) AND MinValue (port < 1)
        errors.ShouldContain(e => e.Contains("port", StringComparison.Ordinal) && e.Contains("required", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateEdits_PortBelowOne_ReturnsError()
    {
        var overrides = new JsonObject
        {
            ["host"] = "localhost",
            ["port"] = -1
        };

        var errors = CapabilityResolver.ValidateEdits("external-target", overrides, isNewApp: true);

        errors.ShouldContain(e => e.Contains("port", StringComparison.Ordinal) && e.Contains("greater than or equal", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateEdits_PortAboveSixtyFiveThousandFiveHundredThirtyFive_ReturnsError()
    {
        var overrides = new JsonObject
        {
            ["host"] = "localhost",
            ["port"] = 65536
        };

        var errors = CapabilityResolver.ValidateEdits("external-target", overrides, isNewApp: true);

        errors.ShouldContain(e => e.Contains("port", StringComparison.Ordinal) && e.Contains("less than or equal", StringComparison.Ordinal));
    }

    // ---- Host pattern: accept (default policy, private-only) ----

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("169.254.1.1")]
    [InlineData("foo.local")]
    [InlineData("bar.lan")]
    [InlineData("crawl4ai.local")]
    public void ValidateEdits_PrivateHost_Accepted(string host)
    {
        var overrides = new JsonObject
        {
            ["host"] = host,
            ["port"] = 8080
        };

        var errors = CapabilityResolver.ValidateEdits("external-target", overrides, isNewApp: true);

        errors.ShouldBeEmpty();
    }

    // ---- Host pattern: reject (default policy, private-only) ----

    [Theory]
    [InlineData("api.openai.com")]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]   // just outside RFC1918 172.16/12
    [InlineData("172.15.0.1")]   // just below RFC1918 172.16/12
    [InlineData("example.com")]
    [InlineData("192.169.1.1")]  // just outside RFC1918 192.168/16
    [InlineData("11.0.0.1")]     // just outside RFC1918 10.0.0.0/8
    public void ValidateEdits_PublicHost_RejectedByDefault(string host)
    {
        var overrides = new JsonObject
        {
            ["host"] = host,
            ["port"] = 8080
        };

        var errors = CapabilityResolver.ValidateEdits("external-target", overrides, isNewApp: true);

        errors.ShouldContain(e => e.Contains("host", StringComparison.Ordinal) && e.Contains("RFC1918", StringComparison.Ordinal));
    }

    // ---- Host pattern: accept when AllowPublicHosts opt-in ----

    [Theory]
    [InlineData("api.openai.com")]
    [InlineData("8.8.8.8")]
    [InlineData("example.com")]
    [InlineData("crawl4ai-public.example.com")]
    public void ValidateEdits_PublicHost_AcceptedWhenAllowPublicHostsTrue(string host)
    {
        var overrides = new JsonObject
        {
            ["host"] = host,
            ["port"] = 8080
        };

        var errors = CapabilityResolver.ValidateEdits
        (
            "external-target", overrides, isNewApp: true, allowPublicHosts: true
        );

        errors.ShouldBeEmpty();
    }

    // ---- Host shape fallback when AllowPublicHosts is on ----

    [Theory]
    [InlineData("foo bar")]                // whitespace inside
    [InlineData("foo@bar")]                // illegal char
    [InlineData("foo/bar")]                // path separator
    public void ValidateEdits_IllegallyShapedHost_RejectedEvenWithAllowPublicHostsTrue(string host)
    {
        var overrides = new JsonObject
        {
            ["host"] = host,
            ["port"] = 8080
        };

        var errors = CapabilityResolver.ValidateEdits
        (
            "external-target", overrides, isNewApp: true, allowPublicHosts: true
        );

        errors.ShouldContain(e => e.Contains("host", StringComparison.Ordinal));
    }

    // ---- Wire-vs-compile regex discipline (per #310 LESSON) ----
    //
    // Wire form ships to the FE via FieldDescriptor.ValuePattern. The FE
    // consumes it via "new RegExp(source)" -- JS regex "$" is strict
    // end-of-input, whereas .NET "$" admits a trailing newline. The wire
    // form MUST use "$" not "\z". Compile-side strict-anchor form lives
    // in ResolveValuePattern's ExternalTargetHostPattern partial-property
    // regex.
    [Fact]
    public void ExternalTargetHostPatternString_WireForm_UsesDollarAnchor()
    {
        var pattern = CapabilityResolver.ExternalTargetHostPatternString;

        pattern.ShouldNotContain(@"\z");
        pattern.ShouldEndWith("$");
    }

    [Fact]
    public void ResolveValuePattern_CompileForm_RejectsTrailingNewline()
    {
        var compiled = CapabilityResolver.ResolveValuePattern
        (
            CapabilityResolver.ExternalTargetHostPatternString
        );

        compiled.ShouldNotBeNull();
        compiled!.IsMatch("localhost").ShouldBeTrue();
        compiled.IsMatch("localhost\n").ShouldBeFalse();
    }

    [Fact]
    public void ResolveValuePattern_AllowPublicHosts_ReturnsNull()
    {
        var compiled = CapabilityResolver.ResolveValuePattern
        (
            CapabilityResolver.ExternalTargetHostPatternString,
            allowPublicHosts: true
        );

        compiled.ShouldBeNull();
    }

    // ---- Configuration round-trip ----

    [Fact]
    public void Resolve_FromBindingsAndOverride_MergesPort()
    {
        var defaults = """{"host":"","port":0,"scheme":"http"}""";
        var overrides = """{"host":"crawl4ai","port":11235}""";

        var resolved = CapabilityResolver.Resolve<ExternalTargetConfiguration>(defaults, overrides);

        resolved.Host.ShouldBe("crawl4ai");
        resolved.Port.ShouldBe(11235);
        resolved.Scheme.ShouldBe("http");
    }

    [Fact]
    public void Resolve_HttpsScheme_PreservedThroughMerge()
    {
        var defaults = """{"host":"","port":0,"scheme":"http"}""";
        var overrides = """{"host":"upstream.local","port":8443,"scheme":"https"}""";

        var resolved = CapabilityResolver.Resolve<ExternalTargetConfiguration>(defaults, overrides);

        resolved.Scheme.ShouldBe("https");
    }
}
