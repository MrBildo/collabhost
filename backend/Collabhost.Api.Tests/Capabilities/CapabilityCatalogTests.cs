using Collabhost.Api.Capabilities;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Capabilities;

public class CapabilityCatalogTests
{
    private static readonly string[] _expectedSlugs =
    [
        "process",
        "port-injection",
        "routing",
        "health-check",
        "environment-defaults",
        "restart",
        "auto-start",
        "artifact"
    ];

    [Fact]
    public void All_ContainsExactlyEightCapabilities() =>
        CapabilityCatalog.All.Count.ShouldBe(8);

    [Theory]
    [MemberData(nameof(AllSlugs))]
    public void All_ContainsExpectedSlug(string slug) =>
        CapabilityCatalog.All.ShouldContainKey(slug);

    [Theory]
    [MemberData(nameof(AllSlugs))]
    public void Get_ReturnsDefinitionForKnownSlug(string slug)
    {
        var definition = CapabilityCatalog.Get(slug);

        definition.ShouldNotBeNull();
        definition.DisplayName.ShouldNotBeNullOrWhiteSpace();
        definition.ConfigurationType.ShouldNotBeNull();
        definition.Schema.ShouldNotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(AllSlugs))]
    public void GetSchema_ReturnsNonEmptySchemaForKnownSlug(string slug)
    {
        var schema = CapabilityCatalog.GetSchema(slug);

        schema.ShouldNotBeNull();
        schema.ShouldNotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(AllSlugs))]
    public void Schema_FieldsHaveUniqueKeys(string slug)
    {
        var schema = CapabilityCatalog.GetSchema(slug)!;

        var keys = schema.Select(f => f.Key).ToList();
        var distinctKeys = keys.Distinct(StringComparer.Ordinal).ToList();

        keys.Count.ShouldBe(distinctKeys.Count, $"Duplicate field keys in {slug} schema.");
    }

    [Fact]
    public void Get_ReturnsNullForUnknownSlug() =>
        CapabilityCatalog.Get("unknown").ShouldBeNull();

    [Fact]
    public void GetSchema_ReturnsNullForUnknownSlug() =>
        CapabilityCatalog.GetSchema("unknown").ShouldBeNull();

    [Fact]
    public void IsKnown_ReturnsTrueForKnownSlug() =>
        CapabilityCatalog.IsKnown("process").ShouldBeTrue();

    [Fact]
    public void IsKnown_ReturnsFalseForUnknownSlug() =>
        CapabilityCatalog.IsKnown("unknown").ShouldBeFalse();

    [Theory]
    [MemberData(nameof(AllSlugs))]
    public void Schema_SelectFields_HaveOptions(string slug)
    {
        var schema = CapabilityCatalog.GetSchema(slug)!;

        foreach (var field in schema.Where(f => f.Type == FieldType.Select))
        {
            field.Options.ShouldNotBeNull($"Select field '{field.Key}' in {slug} must have options.");
            field.Options.ShouldNotBeEmpty($"Select field '{field.Key}' in {slug} must have at least one option.");
        }
    }

    [Theory]
    [InlineData("process", "discoveryStrategy", true)]
    [InlineData("process", "command", true)]
    [InlineData("process", "arguments", true)]
    [InlineData("process", "workingDirectory", true)]
    [InlineData("process", "shutdownTimeoutSeconds", false)]
    [InlineData("process", "startupGracePeriodSeconds", false)]
    [InlineData("process", "maxStartupRetries", false)]
    [InlineData("port-injection", "environmentVariableName", true)]
    [InlineData("port-injection", "portFormat", true)]
    [InlineData("routing", "domainPattern", true)]
    [InlineData("routing", "serveMode", true)]
    [InlineData("routing", "spaFallback", true)]
    [InlineData("artifact", "location", true)]
    [InlineData("environment-defaults", "variables", true)]
    [InlineData("health-check", "endpoint", false)]
    [InlineData("health-check", "intervalSeconds", false)]
    [InlineData("health-check", "timeoutSeconds", false)]
    [InlineData("restart", "policy", false)]
    [InlineData("restart", "successExitCodes", false)]
    [InlineData("auto-start", "enabled", false)]
    public void Schema_RequiresRestart_FlaggedCorrectly
    (
        string capabilitySlug,
        string fieldKey,
        bool expectedRequiresRestart
    )
    {
        var schema = CapabilityCatalog.GetSchema(capabilitySlug);

        schema.ShouldNotBeNull();

        var field = schema.SingleOrDefault
        (
            f => string.Equals(f.Key, fieldKey, StringComparison.Ordinal)
        );

        field.ShouldNotBeNull($"Field '{fieldKey}' not found in {capabilitySlug} schema.");
        field.RequiresRestart.ShouldBe
        (
            expectedRequiresRestart,
            $"Field '{capabilitySlug}.{fieldKey}' RequiresRestart should be {expectedRequiresRestart}."
        );
    }

    public static TheoryData<string> AllSlugs()
    {
        var data = new TheoryData<string>();

        foreach (var slug in _expectedSlugs)
        {
            data.Add(slug);
        }

        return data;
    }
}
