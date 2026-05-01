using System.Text.Json.Nodes;

using Collabhost.Api.Installation;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Installation;

public class AppSettingsMergerTests
{
    private static JsonObject Parse(string json) =>
        (JsonObject)JsonNode.Parse(json)!;

    [Fact]
    public void Merge_OperatorUntouchedKey_RefreshesToNewShippedDefault()
    {
        var shipped = Parse("""{"Hosting":{"ListenPort":58500}}""");
        var current = Parse("""{"Hosting":{"ListenPort":58400}}""");
        var baseline = Parse("""{"Hosting":{"ListenPort":58400}}""");

        var result = AppSettingsMerger.Merge(shipped, current, baseline);

        result.Merged["Hosting"]!["ListenPort"]!.GetValue<int>().ShouldBe(58500);
        result.Changes.ShouldContain(c => c.Path == "Hosting:ListenPort" && c.Kind == MergeChangeKind.RefreshedDefault);
    }

    [Fact]
    public void Merge_OperatorEditedKey_PreservesOperatorValueEvenWhenShippedChanged()
    {
        var shipped = Parse("""{"Hosting":{"ListenPort":58500}}""");
        var current = Parse("""{"Hosting":{"ListenPort":9090}}""");
        var baseline = Parse("""{"Hosting":{"ListenPort":58400}}""");

        var result = AppSettingsMerger.Merge(shipped, current, baseline);

        result.Merged["Hosting"]!["ListenPort"]!.GetValue<int>().ShouldBe(9090);
        result.Changes.ShouldContain(c => c.Path == "Hosting:ListenPort" && c.Kind == MergeChangeKind.PreservedOperatorEdit);
    }

    [Fact]
    public void Merge_NewShippedKey_IsAddedWhenAbsentFromOperatorFile()
    {
        var shipped = Parse("""{"Hosting":{"ListenPort":58400},"Portal":{"Subdomain":"collabhost"}}""");
        var current = Parse("""{"Hosting":{"ListenPort":58400}}""");
        var baseline = Parse("""{"Hosting":{"ListenPort":58400}}""");

        var result = AppSettingsMerger.Merge(shipped, current, baseline);

        result.Merged["Portal"]!["Subdomain"]!.GetValue<string>().ShouldBe("collabhost");
        result.Changes.ShouldContain(c => c.Path == "Portal" && c.Kind == MergeChangeKind.Added);
    }

    [Fact]
    public void Merge_OperatorAddedKey_PreservesEvenWhenAbsentFromShipped()
    {
        var shipped = Parse("""{"Hosting":{"ListenPort":58400}}""");
        var current = Parse("""{"Hosting":{"ListenPort":58400},"OperatorOnly":{"X":1}}""");
        var baseline = Parse("""{"Hosting":{"ListenPort":58400}}""");

        var result = AppSettingsMerger.Merge(shipped, current, baseline);

        result.Merged["OperatorOnly"]!["X"]!.GetValue<int>().ShouldBe(1);
        result.Changes.ShouldContain(c => c.Path == "OperatorOnly" && c.Kind == MergeChangeKind.PreservedExtraKey);
    }

    [Fact]
    public void Merge_NoBaseline_RunsInConservativeMode_KeepsExistingOperatorValues()
    {
        var shipped = Parse("""{"Hosting":{"ListenPort":58500}}""");
        var current = Parse("""{"Hosting":{"ListenPort":58400}}""");

        var result = AppSettingsMerger.Merge(shipped, current, baseline: null);

        result.Merged["Hosting"]!["ListenPort"]!.GetValue<int>().ShouldBe(58400);
        result.Conservative.ShouldBeTrue();
        result.Changes.ShouldContain(c => c.Path == "Hosting:ListenPort" && c.Kind == MergeChangeKind.PreservedConservative);
    }

    [Fact]
    public void Merge_NoBaseline_StillAddsBrandNewKeysFromShipped()
    {
        var shipped = Parse("""{"Hosting":{"ListenPort":58400},"NewSection":{"Key":"value"}}""");
        var current = Parse("""{"Hosting":{"ListenPort":58400}}""");

        var result = AppSettingsMerger.Merge(shipped, current, baseline: null);

        result.Merged["NewSection"]!["Key"]!.GetValue<string>().ShouldBe("value");
        result.Changes.ShouldContain(c => c.Path == "NewSection" && c.Kind == MergeChangeKind.Added);
    }

    [Fact]
    public void Merge_OperatorAndShippedIdentical_EmitsNoRefreshNoise()
    {
        var shipped = Parse("""{"Hosting":{"ListenPort":58400}}""");
        var current = Parse("""{"Hosting":{"ListenPort":58400}}""");
        var baseline = Parse("""{"Hosting":{"ListenPort":58400}}""");

        var result = AppSettingsMerger.Merge(shipped, current, baseline);

        result.Merged["Hosting"]!["ListenPort"]!.GetValue<int>().ShouldBe(58400);
        result.HasChanges.ShouldBeFalse();
    }

    [Fact]
    public void Merge_NestedThreeLevels_RoutesPreserveAndRefreshIndependently()
    {
        var shipped = Parse(
            """
            {
              "Logging": {
                "LogLevel": {
                  "Default": "Warning",
                  "Microsoft.AspNetCore": "Warning"
                }
              }
            }
            """);

        // Operator pinned Default to Debug; left Microsoft.AspNetCore alone.
        var current = Parse(
            """
            {
              "Logging": {
                "LogLevel": {
                  "Default": "Debug",
                  "Microsoft.AspNetCore": "Information"
                }
              }
            }
            """);

        var baseline = Parse(
            """
            {
              "Logging": {
                "LogLevel": {
                  "Default": "Information",
                  "Microsoft.AspNetCore": "Information"
                }
              }
            }
            """);

        var result = AppSettingsMerger.Merge(shipped, current, baseline);

        // Operator's Debug edit is preserved.
        result.Merged["Logging"]!["LogLevel"]!["Default"]!.GetValue<string>().ShouldBe("Debug");

        // Untouched Microsoft.AspNetCore moves from Information -> Warning (refreshed).
        result.Merged["Logging"]!["LogLevel"]!["Microsoft.AspNetCore"]!.GetValue<string>().ShouldBe("Warning");

        result.Changes.ShouldContain(c => c.Path == "Logging:LogLevel:Default" && c.Kind == MergeChangeKind.PreservedOperatorEdit);
        result.Changes.ShouldContain(c => c.Path == "Logging:LogLevel:Microsoft.AspNetCore" && c.Kind == MergeChangeKind.RefreshedDefault);
    }

    [Fact]
    public void Merge_OperatorTypeMismatch_TreatsAsLeafAndPreservesOperatorWithBaseline()
    {
        // Operator changed an object into a string. Treat the operator's choice as authoritative.
        var shipped = Parse("""{"Auth":{"AdminKey":null}}""");
        var current = Parse("""{"Auth":"my-string-key"}""");
        var baseline = Parse("""{"Auth":{"AdminKey":null}}""");

        var result = AppSettingsMerger.Merge(shipped, current, baseline);

        result.Merged["Auth"]!.GetValue<string>().ShouldBe("my-string-key");
        result.Changes.ShouldContain(c => c.Path == "Auth" && c.Kind == MergeChangeKind.PreservedOperatorEdit);
    }

    [Fact]
    public void Merge_NullValueRoundTripsCorrectly()
    {
        var shipped = Parse("""{"Auth":{"AdminKey":null}}""");
        var current = Parse("""{"Auth":{"AdminKey":null}}""");
        var baseline = Parse("""{"Auth":{"AdminKey":null}}""");

        var result = AppSettingsMerger.Merge(shipped, current, baseline);

        result.Merged["Auth"]!["AdminKey"].ShouldBeNull();
        result.HasChanges.ShouldBeFalse();
    }

    [Fact]
    public void Merge_RealAppsettings_PreservesOperatorEditAndRefreshesNewKey()
    {
        // Mirrors the actual v0.1.x -> v0.2.x scenario from the dispatch context: a shipped file
        // gains the Portal section while the operator has pinned a custom AdminKey and changed
        // ListenPort. We expect the operator's edits preserved and the new Portal section added.

        var shipped = Parse(
            """
            {
              "ConnectionStrings": { "Host": "Data Source=./data/collabhost.db" },
              "Auth": { "AdminKey": null },
              "Hosting": { "ListenPort": 58400 },
              "Portal": { "Subdomain": "collabhost" }
            }
            """);

        var current = Parse(
            """
            {
              "ConnectionStrings": { "Host": "Data Source=./data/collabhost.db" },
              "Auth": { "AdminKey": "01HXYZ" },
              "Hosting": { "ListenPort": 9090 }
            }
            """);

        var baseline = Parse(
            """
            {
              "ConnectionStrings": { "Host": "Data Source=./data/collabhost.db" },
              "Auth": { "AdminKey": null },
              "Hosting": { "ListenPort": 58400 }
            }
            """);

        var result = AppSettingsMerger.Merge(shipped, current, baseline);

        result.Merged["Auth"]!["AdminKey"]!.GetValue<string>().ShouldBe("01HXYZ");
        result.Merged["Hosting"]!["ListenPort"]!.GetValue<int>().ShouldBe(9090);
        result.Merged["Portal"]!["Subdomain"]!.GetValue<string>().ShouldBe("collabhost");

        result.Changes.ShouldContain(c => c.Path == "Auth:AdminKey" && c.Kind == MergeChangeKind.PreservedOperatorEdit);
        result.Changes.ShouldContain(c => c.Path == "Hosting:ListenPort" && c.Kind == MergeChangeKind.PreservedOperatorEdit);
        result.Changes.ShouldContain(c => c.Path == "Portal" && c.Kind == MergeChangeKind.Added);
    }

    [Fact]
    public void Merge_NonObjectShippedRoot_Throws()
    {
        var shipped = JsonNode.Parse("[]")!;
        var current = Parse("""{}""");

        Should.Throw<ArgumentException>(() => AppSettingsMerger.Merge(shipped, current, baseline: null));
    }
}
