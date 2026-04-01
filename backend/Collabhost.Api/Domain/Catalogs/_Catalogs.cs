namespace Collabhost.Api.Domain.Catalogs;

public static class IdentifierCatalog
{
    public static class AppTypes
    {
        public static readonly Guid DotNetApp = new("d3333aba-642e-4784-a501-856a25ae6fe5");
        public static readonly Guid NodeApp = new("49d21824-f9e6-4a44-9b12-130f8c680cb9");
        public static readonly Guid Executable = new("bf5105c8-6a99-414c-96b6-c74aab5471f7");
        public static readonly Guid ReactApp = new("73e28a95-764f-4ae5-9c2b-a9fdea66c348");
        public static readonly Guid StaticSite = new("606cdf1f-f41e-42d2-bb13-04b598de0f63");
        public static readonly Guid SystemService = new("a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d");
    }

    public static class Capabilities
    {
        public static readonly Guid Process = new("8460d864-4df5-423d-bb46-c351594b6667");
        public static readonly Guid PortInjection = new("0c66bd0a-d6b1-419d-83c2-9186d773cc52");
        public static readonly Guid Routing = new("6e77364e-d15c-4a02-b12d-9d8a0d623ff3");
        public static readonly Guid HealthCheck = new("76e18bcb-2ee9-4c9c-9d11-fb10c8f20ee0");
        public static readonly Guid EnvironmentDefaults = new("b4fdd637-4e1f-46fb-a5fe-83bccf14a30e");
        public static readonly Guid Restart = new("2661f347-6af3-40a6-a0c4-57fb7e4d1f72");
        public static readonly Guid AutoStart = new("30e21343-9124-4bec-b247-f780a5be12df");
        public static readonly Guid AspNetRuntime = new("1f642072-7975-4fa0-8109-4d9be5ffa909");
        public static readonly Guid NodeRuntime = new("0ba21247-bf12-4487-b2ad-e4c84a784d75");
        public static readonly Guid ReactRuntime = new("2bc51a48-5e56-4c27-958a-615009fea233");
    }

    public static class ProcessStates
    {
        public static readonly Guid Stopped = new("d413f4ed-d764-4277-ab4c-190822d22789");
        public static readonly Guid Starting = new("b9057b8a-7fe7-407c-84e2-dcdc41caeee1");
        public static readonly Guid Running = new("2cd5e4ed-ac0e-40eb-abad-3b56819f97a4");
        public static readonly Guid Stopping = new("0d98c241-6630-4f0f-a16e-bad8a013eb31");
        public static readonly Guid Crashed = new("55331f23-8eae-4e56-811d-8bbdeaca03ca");
        public static readonly Guid Restarting = new("517b2fb1-e5f9-4fdd-98de-10cedec5bcc3");
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
        public const string SystemService = "system-service";
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

    public static class RestartPolicies
    {
        public const string Never = "never";
        public const string OnCrash = "onCrash";
        public const string Always = "always";
    }

    public static class ServeModes
    {
        public const string ReverseProxy = "reverseProxy";
        public const string FileServer = "fileServer";
    }

    public static class DiscoveryStrategies
    {
        public const string DotNetRuntimeConfig = "dotnet-runtimeconfig";
        public const string PackageJson = "package-json";
        public const string Manual = "manual";
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

public static class DisplayNames
{
    public static class RestartPolicies
    {
        public const string Never = "Never";
        public const string OnCrash = "On Crash";
        public const string Always = "Always";
    }

    public static class ServeModes
    {
        public const string ReverseProxy = "Reverse Proxy";
        public const string FileServer = "File Server";
    }

    public static class DiscoveryStrategies
    {
        public const string DotNetRuntimeConfig = ".NET Runtime Config";
        public const string PackageJson = "package.json";
        public const string Manual = "Manual";
    }
}
