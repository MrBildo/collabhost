namespace Collabhost.Api.Domain.Catalogs;

public static class IdentifierCatalog
{
    public static class AppTypes
    {
        public static readonly Guid Executable = new("acdb6994-2c22-42f5-bf89-68c42c9f980c");
        public static readonly Guid NpmPackage = new("d71d5599-bad3-4b28-8920-1aae916bd3cb");
        public static readonly Guid StaticSite = new("7dc8cc9f-1600-447a-85f4-cbc0fc44e6fc");
        public static readonly Guid ProxyService = new("76a3e5b7-d751-4eba-aac2-b1c26c1f9ca3");
    }

    public static class RestartPolicies
    {
        public static readonly Guid Never = new("2f2f6115-b6ef-4db4-b3c7-200a4dbb3408");
        public static readonly Guid OnCrash = new("a5806eba-9dcd-4145-acc3-7bcabd699829");
        public static readonly Guid Always = new("3902811f-674d-483a-9d6b-8b8917d83c0f");
    }

    public static class ProcessStates
    {
        public static readonly Guid Stopped = new("b0a1c2d3-e4f5-6789-abcd-ef0123456789");
        public static readonly Guid Starting = new("c1b2a3d4-f5e6-7890-bcde-f01234567890");
        public static readonly Guid Running = new("d2c3b4a5-0617-8901-cdef-012345678901");
        public static readonly Guid Stopping = new("e3d4c5b6-1728-9012-def0-123456789012");
        public static readonly Guid Crashed = new("f4e5d6c7-2839-0123-ef01-234567890123");
        public static readonly Guid Restarting = new("05f6e7d8-394a-1234-f012-345678901234");
    }
}

// Immutable machine-readable Name constants for lookup entities.
// These match the [Name] column in the database and are used for seeding and code-level references.

public static class StringCatalog
{
    public static class AppTypes
    {
        public const string Executable = "Executable";
        public const string NpmPackage = "NpmPackage";
        public const string StaticSite = "StaticSite";
        public const string ProxyService = "ProxyService";
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
