namespace Collabhost.Api.Domain.Catalogs;

public static class IdentifierCatalog
{
    public static class AppTypes
    {
        public static readonly Guid Executable = new("a1b2c3d4-0001-0001-0001-000000000001");
        public static readonly Guid NpmPackage = new("a1b2c3d4-0001-0001-0001-000000000002");
        public static readonly Guid StaticSite = new("a1b2c3d4-0001-0001-0001-000000000003");
    }

    public static class RestartPolicies
    {
        public static readonly Guid Never = new("a1b2c3d4-0002-0002-0002-000000000001");
        public static readonly Guid OnCrash = new("a1b2c3d4-0002-0002-0002-000000000002");
        public static readonly Guid Always = new("a1b2c3d4-0002-0002-0002-000000000003");
    }
}
