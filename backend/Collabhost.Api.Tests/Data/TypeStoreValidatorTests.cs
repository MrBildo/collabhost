using Collabhost.Api.Data.AppTypes;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Data;

public class TypeStoreValidatorTests
{
    private static readonly string _validJson = """
        {
          "slug": "test-app",
          "displayName": "Test Application",
          "description": "A test app type",
          "bindings": {
            "process": {
              "discoveryStrategy": "Manual",
              "shutdownTimeoutSeconds": 10
            }
          }
        }
        """;

    [Fact]
    public void Validate_ValidSingleFile_ReturnsNoErrors()
    {
        var sources = new List<(string ResourceName, string Json)>
        {
            ("Collabhost.Api.Data.BuiltInTypes.test-app.json", _validJson)
        };

        var errors = TypeStoreValidator.Validate(sources);

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_MalformedJson_ReturnsParseError()
    {
        var sources = new List<(string ResourceName, string Json)>
        {
            ("Collabhost.Api.Data.BuiltInTypes.bad.json", "{ not valid json }")
        };

        var errors = TypeStoreValidator.Validate(sources);

        errors.ShouldNotBeEmpty();
        errors.ShouldContain(error => error.FieldPath == "(root)" && error.Message.Contains("Invalid JSON", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MissingSlug_ReturnsError()
    {
        var json = """
            {
              "displayName": "Test Application"
            }
            """;

        var sources = new List<(string ResourceName, string Json)>
        {
            ("Collabhost.Api.Data.BuiltInTypes.test-app.json", json)
        };

        var errors = TypeStoreValidator.Validate(sources);

        errors.ShouldContain(error => error.FieldPath == "slug");
    }

    [Fact]
    public void Validate_EmptySlug_ReturnsError()
    {
        var json = """
            {
              "slug": "",
              "displayName": "Test Application"
            }
            """;

        var sources = new List<(string ResourceName, string Json)>
        {
            ("Collabhost.Api.Data.BuiltInTypes.test-app.json", json)
        };

        var errors = TypeStoreValidator.Validate(sources);

        errors.ShouldContain(error => error.FieldPath == "slug" && error.Message.Contains("empty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_InvalidSlugPattern_ReturnsError()
    {
        var json = """
            {
              "slug": "Test_App",
              "displayName": "Test Application"
            }
            """;

        var sources = new List<(string ResourceName, string Json)>
        {
            ("Collabhost.Api.Data.BuiltInTypes.test-app.json", json)
        };

        var errors = TypeStoreValidator.Validate(sources);

        errors.ShouldContain(error => error.FieldPath == "slug" && error.Message.Contains("[a-z0-9-]+", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_SlugDoesNotMatchFilename_ReturnsError()
    {
        var json = """
            {
              "slug": "wrong-slug",
              "displayName": "Test Application"
            }
            """;

        var sources = new List<(string ResourceName, string Json)>
        {
            ("Collabhost.Api.Data.BuiltInTypes.test-app.json", json)
        };

        var errors = TypeStoreValidator.Validate(sources);

        errors.ShouldContain(error => error.FieldPath == "slug" && error.Message.Contains("does not match resource name", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MissingDisplayName_ReturnsError()
    {
        var json = """
            {
              "slug": "test-app"
            }
            """;

        var sources = new List<(string ResourceName, string Json)>
        {
            ("Collabhost.Api.Data.BuiltInTypes.test-app.json", json)
        };

        var errors = TypeStoreValidator.Validate(sources);

        errors.ShouldContain(error => error.FieldPath == "displayName");
    }

    [Fact]
    public void Validate_UnknownCapabilitySlug_ReturnsError()
    {
        var json = """
            {
              "slug": "test-app",
              "displayName": "Test Application",
              "bindings": {
                "unknown-capability": {
                  "foo": "bar"
                }
              }
            }
            """;

        var sources = new List<(string ResourceName, string Json)>
        {
            ("Collabhost.Api.Data.BuiltInTypes.test-app.json", json)
        };

        var errors = TypeStoreValidator.Validate(sources);

        errors.ShouldContain(error => error.FieldPath == "bindings.unknown-capability" && error.Message.Contains("Unknown capability slug", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_DuplicateSlugs_ReturnsError()
    {
        var json1 = """
            {
              "slug": "test-app",
              "displayName": "Test Application 1"
            }
            """;

        var json2 = """
            {
              "slug": "test-app",
              "displayName": "Test Application 2"
            }
            """;

        var sources = new List<(string ResourceName, string Json)>
        {
            ("Collabhost.Api.Data.BuiltInTypes.test-app.json", json1),
            ("Collabhost.Api.Data.BuiltInTypes.test-app.json", json2)
        };

        var errors = TypeStoreValidator.Validate(sources);

        errors.ShouldContain(error => error.FieldPath == "slug" && error.Message.Contains("Duplicate slug", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_DuplicateDisplayNames_ReturnsError()
    {
        var json1 = """
            {
              "slug": "app-one",
              "displayName": "Same Name"
            }
            """;

        var json2 = """
            {
              "slug": "app-two",
              "displayName": "Same Name"
            }
            """;

        var sources = new List<(string ResourceName, string Json)>
        {
            ("Collabhost.Api.Data.BuiltInTypes.app-one.json", json1),
            ("Collabhost.Api.Data.BuiltInTypes.app-two.json", json2)
        };

        var errors = TypeStoreValidator.Validate(sources);

        errors.ShouldContain(error => error.FieldPath == "displayName" && error.Message.Contains("Duplicate display name", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MultipleErrors_CollectsAll()
    {
        var json = """
            {
              "slug": "INVALID",
              "bindings": {
                "unknown-cap": {}
              }
            }
            """;

        var sources = new List<(string ResourceName, string Json)>
        {
            ("Collabhost.Api.Data.BuiltInTypes.test-app.json", json)
        };

        var errors = TypeStoreValidator.Validate(sources);

        // Should have at least: invalid slug pattern, slug doesn't match filename, missing displayName, unknown capability
        errors.Count.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void Validate_NoBindings_IsValid()
    {
        var json = """
            {
              "slug": "simple-app",
              "displayName": "Simple Application"
            }
            """;

        var sources = new List<(string ResourceName, string Json)>
        {
            ("Collabhost.Api.Data.BuiltInTypes.simple-app.json", json)
        };

        var errors = TypeStoreValidator.Validate(sources);

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void ExtractSlugFromResourceName_ExtractsCorrectSlug()
    {
        var result = TypeStoreValidator.ExtractSlugFromResourceName("Collabhost.Api.Data.BuiltInTypes.dotnet-app.json");

        result.ShouldBe("dotnet-app");
    }

    [Fact]
    public void ExtractSlugFromResourceName_HandlesSimpleName()
    {
        var result = TypeStoreValidator.ExtractSlugFromResourceName("test.json");

        result.ShouldBe("test");
    }

    [Fact]
    public void ValidateUserTypes_ValidUserType_ReturnsNoErrors()
    {
        var userJson = """
            {
              "slug": "custom-app",
              "displayName": "Custom Application"
            }
            """;

        var userSources = new List<(string FileName, string Json)>
        {
            ("custom-app.json", userJson)
        };

        var builtInTypes = new List<AppType>
        {
            new() { Slug = "dotnet-app", DisplayName = ".NET Application", IsBuiltIn = true }
        };

        var errors = TypeStoreValidator.ValidateUserTypes(userSources, builtInTypes);

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void ValidateUserTypes_SlugConflictsWithBuiltIn_ReturnsError()
    {
        var userJson = """
            {
              "slug": "dotnet-app",
              "displayName": "My Custom Type"
            }
            """;

        var userSources = new List<(string FileName, string Json)>
        {
            ("dotnet-app.json", userJson)
        };

        var builtInTypes = new List<AppType>
        {
            new() { Slug = "dotnet-app", DisplayName = ".NET Application", IsBuiltIn = true }
        };

        var errors = TypeStoreValidator.ValidateUserTypes(userSources, builtInTypes);

        errors.ShouldContain(error =>
            error.FieldPath == "slug"
            && error.Message.Contains("conflicts with a built-in type", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateUserTypes_DisplayNameConflictsWithBuiltIn_ReturnsError()
    {
        var userJson = """
            {
              "slug": "unique-slug",
              "displayName": ".NET Application"
            }
            """;

        var userSources = new List<(string FileName, string Json)>
        {
            ("unique-slug.json", userJson)
        };

        var builtInTypes = new List<AppType>
        {
            new() { Slug = "dotnet-app", DisplayName = ".NET Application", IsBuiltIn = true }
        };

        var errors = TypeStoreValidator.ValidateUserTypes(userSources, builtInTypes);

        errors.ShouldContain(error =>
            error.FieldPath == "displayName"
            && error.Message.Contains("conflicts with a built-in type", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateUserTypes_DuplicateSlugsAmongUserTypes_ReturnsError()
    {
        var json1 = """
            {
              "slug": "custom-app",
              "displayName": "Custom App 1"
            }
            """;

        var json2 = """
            {
              "slug": "custom-app",
              "displayName": "Custom App 2"
            }
            """;

        var userSources = new List<(string FileName, string Json)>
        {
            ("custom-app.json", json1),
            ("custom-app.json", json2)
        };

        var builtInTypes = new List<AppType>();

        var errors = TypeStoreValidator.ValidateUserTypes(userSources, builtInTypes);

        errors.ShouldContain(error =>
            error.FieldPath == "slug"
            && error.Message.Contains("Duplicate slug", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateUserTypes_InvalidJson_ReturnsError()
    {
        var userSources = new List<(string FileName, string Json)>
        {
            ("bad-app.json", "{ broken json")
        };

        var builtInTypes = new List<AppType>();

        var errors = TypeStoreValidator.ValidateUserTypes(userSources, builtInTypes);

        errors.ShouldContain(error =>
            error.FieldPath == "(root)"
            && error.Message.Contains("Invalid JSON", StringComparison.Ordinal));
    }
}
