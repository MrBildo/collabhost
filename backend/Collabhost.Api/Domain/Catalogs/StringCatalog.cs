namespace Collabhost.Api.Domain.Catalogs;

// Immutable machine-readable Name constants for lookup entities.
// These match the [Name] column in the database and are used for seeding and code-level references.

public static class StringCatalog
{
    public static class AppTypes
    {
        public const string Executable = "Executable";
        public const string NpmPackage = "NpmPackage";
        public const string StaticSite = "StaticSite";
    }

    public static class RestartPolicies
    {
        public const string Never = "Never";
        public const string OnCrash = "OnCrash";
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
