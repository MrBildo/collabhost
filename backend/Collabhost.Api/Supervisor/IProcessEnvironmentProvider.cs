namespace Collabhost.Api.Supervisor;

// Allows subsystems to contribute environment variables to a managed app's child
// process at start time. The supervisor invokes each registered provider before
// spawning the process and merges contributions into the child's environment.
//
// Sibling to IProcessArgumentProvider. Exists so secrets that live in Collabhost's
// own host process env (e.g. a Cloudflare API token) can flow into a managed
// child's env without ever touching the database.
public interface IProcessEnvironmentProvider
{
    IReadOnlyDictionary<string, string> ContributeEnvironment(string appSlug);
}
