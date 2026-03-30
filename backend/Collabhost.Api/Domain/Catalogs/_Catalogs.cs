namespace Collabhost.Api.Domain.Catalogs;

public static class IdentifierCatalog
{
    public static class AppTypes
    {
        public static readonly Guid DotNetApp = new("b1a2c3d4-e5f6-7890-abcd-ef0123456001");
        public static readonly Guid NodeApp = new("b1a2c3d4-e5f6-7890-abcd-ef0123456002");
        public static readonly Guid Executable = new("b1a2c3d4-e5f6-7890-abcd-ef0123456003");
        public static readonly Guid ReactApp = new("b1a2c3d4-e5f6-7890-abcd-ef0123456004");
        public static readonly Guid StaticSite = new("b1a2c3d4-e5f6-7890-abcd-ef0123456005");
    }

    public static class Capabilities
    {
        public static readonly Guid Process = new("c2b3a4d5-f6e7-8901-bcde-f01234560001");
        public static readonly Guid PortInjection = new("c2b3a4d5-f6e7-8901-bcde-f01234560002");
        public static readonly Guid Routing = new("c2b3a4d5-f6e7-8901-bcde-f01234560003");
        public static readonly Guid HealthCheck = new("c2b3a4d5-f6e7-8901-bcde-f01234560004");
        public static readonly Guid EnvironmentDefaults = new("c2b3a4d5-f6e7-8901-bcde-f01234560005");
        public static readonly Guid Restart = new("c2b3a4d5-f6e7-8901-bcde-f01234560006");
        public static readonly Guid AutoStart = new("c2b3a4d5-f6e7-8901-bcde-f01234560007");
        public static readonly Guid AspNetRuntime = new("c2b3a4d5-f6e7-8901-bcde-f01234560008");
        public static readonly Guid NodeRuntime = new("c2b3a4d5-f6e7-8901-bcde-f01234560009");
        public static readonly Guid ReactRuntime = new("c2b3a4d5-f6e7-8901-bcde-f01234560010");
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
        public const string DotNetApp = "dotnet-app";
        public const string NodeApp = "node-app";
        public const string Executable = "executable";
        public const string ReactApp = "react-app";
        public const string StaticSite = "static-site";
    }

    public static class Capabilities
    {
        public const string Process = "process";
        public const string PortInjection = "port-injection";
        public const string Routing = "routing";
        public const string HealthCheck = "health-check";
        public const string EnvironmentDefaults = "environment-defaults";
        public const string Restart = "restart";
        public const string AutoStart = "auto-start";
        public const string AspNetRuntime = "aspnet-runtime";
        public const string NodeRuntime = "node-runtime";
        public const string ReactRuntime = "react-runtime";
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
