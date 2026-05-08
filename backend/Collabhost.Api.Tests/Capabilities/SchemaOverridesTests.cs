using System.Text.Json.Nodes;

using Collabhost.Api.Capabilities;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Capabilities;

public class SchemaOverridesTests
{
    private static readonly FieldDescriptor _baseDescriptor = new
    (
        "variables",
        "Environment Variables",
        FieldType.KeyValue,
        new FieldEditableAlways(),
        Required: false,
        RequiresRestart: true,
        HelpText: "Generic help text.",
        Unit: null
    );

    [Fact]
    public void Extract_BindingWithoutSchemaOverrides_ReturnsEmpty()
    {
        var bindingJson = """{"variables":{}}""";

        var result = SchemaOverrides.Extract(bindingJson);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void Extract_BindingWithSchemaOverrides_ReturnsFieldOverrideMap()
    {
        var bindingJson = """
            {
              "variables": {},
              "schemaOverrides": {
                "variables": { "helpText": "Type-specific help text." }
              }
            }
            """;

        var result = SchemaOverrides.Extract(bindingJson);

        result.Count.ShouldBe(1);
        result.ShouldContainKey("variables");

        var fieldOverride = result["variables"];

        fieldOverride.ShouldContainKey("helpText");
        fieldOverride["helpText"]!.GetValue<string>().ShouldBe("Type-specific help text.");
    }

    [Fact]
    public void Extract_MalformedJson_ReturnsEmpty()
    {
        var bindingJson = "{ this is not valid json";

        var result = SchemaOverrides.Extract(bindingJson);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void Extract_NonObjectSchemaOverrides_ReturnsEmpty()
    {
        var bindingJson = """{"variables":{},"schemaOverrides":"not-an-object"}""";

        var result = SchemaOverrides.Extract(bindingJson);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void Extract_NonObjectFieldOverride_SkipsThatField()
    {
        var bindingJson = """
            {
              "schemaOverrides": {
                "variables": { "helpText": "ok" },
                "other": "not-an-object"
              }
            }
            """;

        var result = SchemaOverrides.Extract(bindingJson);

        result.Count.ShouldBe(1);
        result.ShouldContainKey("variables");
        result.ShouldNotContainKey("other");
    }

    [Fact]
    public void Apply_NullOverride_ReturnsDescriptorUnchanged()
    {
        var result = SchemaOverrides.Apply(_baseDescriptor, null);

        result.ShouldBe(_baseDescriptor);
    }

    [Fact]
    public void Apply_EmptyOverride_ReturnsDescriptorUnchanged()
    {
        var result = SchemaOverrides.Apply(_baseDescriptor, []);

        result.ShouldBe(_baseDescriptor);
    }

    [Fact]
    public void Apply_HelpTextOverride_OverridesOnlyHelpText()
    {
        var fieldOverride = new JsonObject
        {
            ["helpText"] = "Caddy-specific help text."
        };

        var result = SchemaOverrides.Apply(_baseDescriptor, fieldOverride);

        result.HelpText.ShouldBe("Caddy-specific help text.");
        result.Label.ShouldBe(_baseDescriptor.Label);
        result.Type.ShouldBe(_baseDescriptor.Type);
        result.RequiresRestart.ShouldBe(_baseDescriptor.RequiresRestart);
        result.Required.ShouldBe(_baseDescriptor.Required);
        result.Unit.ShouldBe(_baseDescriptor.Unit);
        result.Editable.ShouldBe(_baseDescriptor.Editable);
        result.Key.ShouldBe(_baseDescriptor.Key);
    }

    [Fact]
    public void Apply_LabelOverride_OverridesOnlyLabel()
    {
        var fieldOverride = new JsonObject
        {
            ["label"] = "Custom Label"
        };

        var result = SchemaOverrides.Apply(_baseDescriptor, fieldOverride);

        result.Label.ShouldBe("Custom Label");
        result.HelpText.ShouldBe(_baseDescriptor.HelpText);
    }

    [Fact]
    public void Apply_MultipleProperties_OverridesAll()
    {
        var fieldOverride = new JsonObject
        {
            ["helpText"] = "Custom help.",
            ["unit"] = "seconds",
            ["requiresRestart"] = false,
            ["required"] = true
        };

        var result = SchemaOverrides.Apply(_baseDescriptor, fieldOverride);

        result.HelpText.ShouldBe("Custom help.");
        result.Unit.ShouldBe("seconds");
        result.RequiresRestart.ShouldBeFalse();
        result.Required.ShouldBeTrue();
        result.Label.ShouldBe(_baseDescriptor.Label);
    }

    [Fact]
    public void Apply_NonStringHelpText_FallsBackToBase()
    {
        var fieldOverride = new JsonObject
        {
            ["helpText"] = 42
        };

        var result = SchemaOverrides.Apply(_baseDescriptor, fieldOverride);

        result.HelpText.ShouldBe(_baseDescriptor.HelpText);
    }

    [Fact]
    public void Apply_NonBoolRequiresRestart_FallsBackToBase()
    {
        var fieldOverride = new JsonObject
        {
            ["requiresRestart"] = "yes"
        };

        var result = SchemaOverrides.Apply(_baseDescriptor, fieldOverride);

        result.RequiresRestart.ShouldBe(_baseDescriptor.RequiresRestart);
    }
}
