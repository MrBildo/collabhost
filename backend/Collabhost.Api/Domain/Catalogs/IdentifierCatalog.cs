namespace Collabhost.Api.Domain.Catalogs;

public static class IdentifierCatalog
{
    public static class AppTypes
    {
        public static readonly Guid Executable = new("acdb6994-2c22-42f5-bf89-68c42c9f980c");
        public static readonly Guid NpmPackage = new("d71d5599-bad3-4b28-8920-1aae916bd3cb");
        public static readonly Guid StaticSite = new("7dc8cc9f-1600-447a-85f4-cbc0fc44e6fc");
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
