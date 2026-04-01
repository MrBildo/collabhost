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
        public static readonly Guid SystemService = new("56608f77-aa9d-44b7-a3cb-df7c361d8fb8");
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
        public static readonly Guid Artifact = new("cdd957fc-0402-45fe-9a37-788545a2ea91");
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

    public static class RestartPolicies
    {
        public static readonly Guid Never = new("efecc013-d65f-407c-af7a-ab61043e00b2");
        public static readonly Guid OnCrash = new("16657ec0-d027-497e-9bbf-eb835492f80b");
        public static readonly Guid Always = new("89ff36ea-1f8a-42a2-a02d-b33b3dd2918c");
    }

    public static class ServeModes
    {
        public static readonly Guid ReverseProxy = new("56d426ec-c60b-449f-a62a-f294bb893fda");
        public static readonly Guid FileServer = new("47a7a3f0-9ab5-4194-8b1a-ee1267cb844c");
    }

    public static class DiscoveryStrategies
    {
        public static readonly Guid DotNetRuntimeConfig = new("f3ee2904-4e01-483f-ad94-e8237953fcfc");
        public static readonly Guid PackageJson = new("506cfcc2-b806-43c3-9baf-593053d9826e");
        public static readonly Guid Manual = new("59e2a600-f739-470b-9981-cfe538af272b");
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
        public const string Artifact = "artifact";
    }

    public static class RestartPolicies
    {
        public const string Never = "never";
        public const string OnCrash = "on-crash";
        public const string Always = "always";
    }

    public static class ServeModes
    {
        public const string ReverseProxy = "reverse-proxy";
        public const string FileServer = "file-server";
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

