using Collabhost.Api.Supervisor;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Supervisor;

// Card #330 -- hosted children must NOT inherit Collabhost's own host-scoped
// environment (ASPNETCORE_CONTENTROOT, ASPNETCORE_ENVIRONMENT,
// DOTNET_ENVIRONMENT, COLLABHOST_*, DOTNET_BUNDLE_EXTRACT_BASE_DIR). The systemd
// unit / Windows service sets those for Collabhost ITSELF; when they leaked into
// a hosted ASP.NET child the child resolved ContentRoot to Collabhost's install
// dir, missed its own appsettings*.json, and aborted (SIGABRT / exit 134).
//
// ChildProcessEnvironment.Build is the single shared told-input contract both
// runners route through. These tests poison the current process environment
// with the exact #330 leak set and assert the built child env excludes them
// unless the curated (capability/operator-override) dictionary carries them.
[Collection(nameof(EnvironmentPoisoningCollection))]
public class ChildProcessEnvironmentTests
{
    // The exact variables the production systemd unit sets on the supervisor
    // (collabhost.service [Service] Environment= lines + ASPNETCORE_CONTENTROOT).
    private static readonly (string Key, string Value)[] _hostLeakSet =
    [
        ("ASPNETCORE_CONTENTROOT", "/opt/collabhost"),
        ("ASPNETCORE_ENVIRONMENT", "Production"),
        ("DOTNET_ENVIRONMENT", "Production"),
        ("DOTNET_BUNDLE_EXTRACT_BASE_DIR", "/var/lib/collabhost/dotnet-bundle"),
        ("COLLABHOST_DATA_PATH", "/var/lib/collabhost/data"),
        ("COLLABHOST_CONFIG_PATH", "/etc/collabhost/appsettings.json"),
        ("COLLABHOST_LOGS_PATH", "/var/log/collabhost"),
        ("COLLABHOST_PROXY_STORAGE_PATH", "/var/lib/collabhost/caddy"),
        ("COLLABHOST_USER_TYPES_PATH", "/var/lib/collabhost/user-types")
    ];

    [Fact]
    public void Build_DoesNotInheritCollabhostHostEnvironment_When330LeakSetIsPresent()
    {
        using var poison = new EnvironmentPoison(_hostLeakSet);

        // Curated set = exactly what the supervisor's MergeEnvironmentVariables
        // produces for a typical hosted app: a port-injection var, nothing else.
        var curated = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ASPNETCORE_URLS"] = "http://localhost:40123"
        };

        var built = ChildProcessEnvironment.Build(curated);

        foreach (var (key, _) in _hostLeakSet)
        {
            built.ContainsKey(key).ShouldBeFalse
            (
                $"'{key}' is a Collabhost-host var; it must NOT leak into a hosted child (#330)"
            );
        }

        // The curated value the supervisor decided on IS present -- it is the
        // told input.
        built["ASPNETCORE_URLS"].ShouldBe("http://localhost:40123");
    }

    [Fact]
    public void Build_HonorsCuratedValue_WhenOperatorSetAHostLeakKeyViaCapability()
    {
        // An operator who deliberately pins ASPNETCORE_ENVIRONMENT via the
        // environment-defaults capability must win: that key arrives in the
        // curated dictionary and the child must see THAT value, not the host's.
        using var poison = new EnvironmentPoison(_hostLeakSet);

        var curated = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Staging"
        };

        var built = ChildProcessEnvironment.Build(curated);

        built["ASPNETCORE_ENVIRONMENT"].ShouldBe("Staging");

        // The other leak-set keys are still absent (only the operator-pinned one
        // is honored).
        built.ContainsKey("ASPNETCORE_CONTENTROOT").ShouldBeFalse();
    }

    [Fact]
    public void Build_KeepsOsContextAllowlist_SoSpawnedProcessCanStart()
    {
        // PATH must survive -- without it the runner cannot resolve `dotnet`.
        // It is OS context, not Collabhost identity, so it is allowlisted.
        var originalPath = Environment.GetEnvironmentVariable("PATH");

        originalPath.ShouldNotBeNull("test host must have PATH set");

        var built = ChildProcessEnvironment.Build
        (
            new Dictionary<string, string>(StringComparer.Ordinal)
        );

        built.ContainsKey("PATH").ShouldBeTrue("PATH is OS context and must be allowlisted");
        built["PATH"].ShouldBe(originalPath);
    }

    [Fact]
    public void Build_CuratedValueWinsOverAllowlistedOsVar_OnKeyConflict()
    {
        // If the operator/app type sets PATH via the capability, the curated
        // value is the told input and wins over the inherited OS PATH.
        var curated = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PATH"] = "/curated/only/path"
        };

        var built = ChildProcessEnvironment.Build(curated);

        built["PATH"].ShouldBe("/curated/only/path");
    }
}
