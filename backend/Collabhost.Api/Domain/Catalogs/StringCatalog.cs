namespace Collabhost.Api.Domain.Catalogs;

// Display name constants. Stubbed in — the canonical display names live in the database.
// Use these for seeding and for cases where you need a display name without a DB round-trip.

public static class StringCatalog
{
    public static class AppTypes
    {
        public const string Executable = "Executable";
        public const string NpmPackage = "NPM Package";
        public const string StaticSite = "Static Site";
    }

    public static class RestartPolicies
    {
        public const string Never = "Never";
        public const string OnCrash = "On Crash";
        public const string Always = "Always";
    }

    public static class ProcessStates
    {
        public const string Stopped = "Stopped";
        public const string Starting = "Starting";
        public const string Running = "Running";
        public const string Stopping = "Stopping";
        public const string Crashed = "Crashed";
        public const string Restarting = "Restarting";
    }
}
