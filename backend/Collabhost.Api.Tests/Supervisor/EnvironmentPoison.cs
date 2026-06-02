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
//
// Note on the #354/#378 isolation invariant (canonical statement: ApiFixture.cs, "Api" collection):
// the members here poison the SAME two vars (COLLABHOST_DATA_PATH / ASPNETCORE_CONTENTROOT) as
// UpdateHostsCliTests does, and DisableParallelization serializes them only WITHIN this collection --
// it does NOT serialize them against the "Api" collection, which runs in parallel with this one.
// They are nonetheless safe to stay out of "Api" because they point at STATIC, never-deleted paths
// (/opt/collabhost, /var/lib/collabhost/data, Path.GetTempPath()): the *ProcessRunnerEnvironmentIsolationTests
// even hold the poisoned value across a multi-second real subprocess spawn-and-await, but a concurrent
// "Api" host boot that resolved one of those paths fails DETERMINISTICALLY (StartupPreflight halt on a
// non-writable path), never the intermittent create-then-DELETE flake that motivated #354. The flake
// requires a path that gets torn down mid-run; a consistently-absent or consistently-writable path
// cannot produce it.
//
// FORWARD AWARENESS: this dormancy is conditional on the static-path property. If any member here is
// ever extended to (a) boot a Program.Main host or a subprocess that resolves these vars AND (b) point
// them at a create-then-delete dir, the #354 flake resurfaces from a poisoner OUTSIDE "Api". At that
// point that test must join [Collection("Api")] per the invariant -- do not give it its own
// DisableParallelization collection (that protects only against itself, not the Api boots).
[CollectionDefinition(nameof(EnvironmentPoisoningCollection), DisableParallelization = true)]
public sealed class EnvironmentPoisoningCollection;
