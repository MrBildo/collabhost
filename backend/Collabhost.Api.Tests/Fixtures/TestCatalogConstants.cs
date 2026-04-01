namespace Collabhost.Api.Tests.Fixtures;

/// <summary>
/// Seeded ExternalId values for built-in app types. These match the migration seed data
/// and are only needed by integration tests that reference app types by external ID.
/// </summary>
public static class TestCatalogConstants
{
    public static class AppTypes
    {
        public const string DotNetAppExternalId = "01KN0P7JYNYACWC35R77C1KTV2";
        public const string NodeAppExternalId = "01KN0P7JYNRBD8DC9DMKEDJX2M";
        public const string ExecutableExternalId = "01KN0P7JYNJRAHGC01N17NFTWW";
        public const string ReactAppExternalId = "01KN0P7JYNM6PJP07XTAXK77GR";
        public const string StaticSiteExternalId = "01KN0P7JYN9TDB3SPPS25Z493F";
        public const string SystemServiceExternalId = "01KN0P7JYN5QSVC3SYSTEM0SVC";
    }
}
