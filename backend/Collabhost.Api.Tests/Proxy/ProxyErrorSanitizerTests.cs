using Collabhost.Api.Proxy;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Proxy;

// Card #426 (FE-XT-03): ProxyErrorSanitizer scrubs the reverse-proxy vendor name out of the
// sync-error string before it crosses the /status boundary as proxyDetail.lastSyncError.
// The property is two-sided -- the vendor name must go, and the diagnostic detail must stay --
// so the cases below assert BOTH for each shape that can reach LastSyncError.
public class ProxyErrorSanitizerTests
{
    // Every shape FormatSyncError / CaddyClient / the outer catch can produce, paired with the
    // diagnostic fragment that must survive the scrub.
    public static TheoryData<string, string> VendorLeakingErrors =>
        new()
        {
            // FormatSyncError: status + body (the production-typical shape, real bind error body)
            {
                "Caddy admin API returned 400: loading config: listening on :443: bind: permission denied",
                "loading config: listening on :443: bind: permission denied"
            },
            // FormatSyncError: status only
            { "Caddy admin API returned 502", "502" },
            // FormatSyncError: no-status fallback
            { "Caddy admin API rejected the route configuration", "rejected the route configuration" },
            // CaddyClient.LoadConfigAsync timeout string (flows in as ErrorBody)
            { "Request timed out contacting Caddy admin API", "Request timed out" },
            // A raw HttpRequestException-style transport message that happens to name the vendor
            { "Connection refused contacting caddy at localhost:2019", "localhost:2019" },
            // The vendor name embedded inside the vendor's own response body
            { "caddy: error during parse: invalid directive", "error during parse: invalid directive" }
        };

    [Theory]
    [MemberData(nameof(VendorLeakingErrors))]
    public void Sanitize_RemovesVendorName_PreservesDiagnosticDetail(string input, string survivingDetail)
    {
        var result = ProxyErrorSanitizer.Sanitize(input);

        result.ShouldNotBeNull();
        result.ShouldNotContain("caddy", Case.Insensitive);
        result.ShouldContain(survivingDetail);
    }

    [Fact]
    public void Sanitize_ReplacesVendorNameWithProxy()
    {
        var result = ProxyErrorSanitizer.Sanitize("Caddy admin API returned 400");

        result.ShouldBe("Proxy admin API returned 400");
    }

    [Theory]
    [InlineData("Caddy")]
    [InlineData("caddy")]
    [InlineData("CADDY")]
    [InlineData("cAdDy")]
    public void Sanitize_ScrubsEveryCasing(string vendor)
    {
        var result = ProxyErrorSanitizer.Sanitize($"{vendor} admin API failed");

        result.ShouldNotBeNull();
        result.ShouldNotContain("caddy", Case.Insensitive);
        result.ShouldContain("admin API failed");
    }

    [Fact]
    public void Sanitize_NonVendorError_PassesThroughUnchanged()
    {
        // An error with no vendor name keeps its full diagnostic value untouched.
        const string error = "loading config: listening on :443: bind: permission denied";

        var result = ProxyErrorSanitizer.Sanitize(error);

        result.ShouldBe(error);
    }

    [Fact]
    public void Sanitize_WordBounded_DoesNotMangleUnrelatedSubstrings()
    {
        // "caddyshack" is not the vendor token -- a word-bounded scrub must leave it alone so
        // a path or identifier that merely contains the letters is never corrupted.
        const string error = "failed reading /opt/caddyshack/config";

        var result = ProxyErrorSanitizer.Sanitize(error);

        result.ShouldBe(error);
    }

    [Fact]
    public void Sanitize_Null_ReturnsNull() =>
        ProxyErrorSanitizer.Sanitize(null).ShouldBeNull();

    [Fact]
    public void Sanitize_Empty_ReturnsEmpty() =>
        ProxyErrorSanitizer.Sanitize(string.Empty).ShouldBe(string.Empty);
}
