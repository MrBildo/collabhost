using Xunit;

namespace Collabhost.Api.Tests.Supervisor;

// Sets process-global environment variables for the lifetime of the instance,
// then restores their prior values (or unsets them) on dispose. Used by the
// #330 regression tests to simulate the supervisor running with Collabhost's
// own host vars set (as the systemd unit / Windows service does) and prove a
// spawned child does NOT inherit them.
//
// Process environment is global mutable state; tests that use this MUST be in
// the EnvironmentPoisoningCollection so xUnit serializes them.
internal sealed class EnvironmentPoison : IDisposable
{
    private readonly (string Key, string? Original)[] _restore;

    public EnvironmentPoison(params (string Key, string Value)[] variables)
    {
        ArgumentNullException.ThrowIfNull(variables);

        _restore = new (string, string?)[variables.Length];

        for (var i = 0; i < variables.Length; i++)
        {
            var (key, value) = variables[i];

            _restore[i] = (key, Environment.GetEnvironmentVariable(key));

            Environment.SetEnvironmentVariable(key, value);
        }
    }

    public void Dispose()
    {
        foreach (var (key, original) in _restore)
        {
            // Setting to null unsets the variable -- correct when it was absent
            // before poisoning.
            Environment.SetEnvironmentVariable(key, original);
        }
    }
}

// xUnit collection: serializes every test class that mutates the process-global
// environment so concurrent poisoning cannot race.
[CollectionDefinition(nameof(EnvironmentPoisoningCollection), DisableParallelization = true)]
public sealed class EnvironmentPoisoningCollection;
