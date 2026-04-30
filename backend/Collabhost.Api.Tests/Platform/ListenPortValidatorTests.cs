using Collabhost.Api.Platform;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Platform;

// Tests for the cross-validation between Hosting:ListenPort (Caddy dial target) and Kestrel's
// actual listen port. Card #165 -- soft warning, not fatal. Pure-function shape on
// ListenPortValidator means we can exercise both branches without spinning up a host.
public class ListenPortValidatorTests
{
    [Fact]
    public void Validate_PortMatches_ReportsMatch()
    {
        var outcome = ListenPortValidator.Validate
        (
            configuredListenPort: 58400,
            listeningAddresses: ["http://localhost:58400"]
        );

        outcome.Status.ShouldBe(ListenPortValidationStatus.Match);
        outcome.ConfiguredListenPort.ShouldBe(58400);
        outcome.RenderedMessage.ShouldBeNull();
    }

    [Fact]
    public void Validate_PortMatchesAmongMultipleAddresses_ReportsMatch()
    {
        // Kestrel can bind to multiple URLs at once (e.g. http+https). A match on any of
        // them is enough -- Caddy can reach ListenPort.
        var outcome = ListenPortValidator.Validate
        (
            configuredListenPort: 58400,
            listeningAddresses:
            [
                "http://localhost:58400",
                "https://localhost:58401"
            ]
        );

        outcome.Status.ShouldBe(ListenPortValidationStatus.Match);
    }

    [Fact]
    public void Validate_PortMismatch_ReportsMismatchWithBothValues()
    {
        var outcome = ListenPortValidator.Validate
        (
            configuredListenPort: 58400,
            listeningAddresses: ["http://localhost:5000"]
        );

        outcome.Status.ShouldBe(ListenPortValidationStatus.Mismatch);
        outcome.ConfiguredListenPort.ShouldBe(58400);
        outcome.ObservedPorts.ShouldBe([5000]);

        // The warning copy is operator-facing -- it must name both observed values and
        // both env-var levers so the next step is actionable from the log line alone.
        var message = outcome.RenderedMessage.ShouldNotBeNull();
        message.ShouldContain("58400");
        message.ShouldContain("5000");
        message.ShouldContain("COLLABHOST_HOSTING_LISTEN_PORT");
        message.ShouldContain("ASPNETCORE_URLS");
        message.ShouldContain("502");
    }

    [Fact]
    public void Validate_PortMismatchMultipleObserved_ReportsAllObservedPorts()
    {
        var outcome = ListenPortValidator.Validate
        (
            configuredListenPort: 58400,
            listeningAddresses:
            [
                "http://localhost:5000",
                "https://localhost:5001"
            ]
        );

        outcome.Status.ShouldBe(ListenPortValidationStatus.Mismatch);
        outcome.ObservedPorts.ShouldBe([5000, 5001]);
        var message = outcome.RenderedMessage.ShouldNotBeNull();
        message.ShouldContain("5000");
        message.ShouldContain("5001");
    }

    [Fact]
    public void Validate_NoListeningAddresses_SkipsValidation()
    {
        // TestServer (WebApplicationFactory<Program>) does not populate
        // IServerAddressesFeature.Addresses. The validator must short-circuit cleanly --
        // a mismatch warning under the test harness would be a false positive.
        var outcome = ListenPortValidator.Validate
        (
            configuredListenPort: 58400,
            listeningAddresses: []
        );

        outcome.Status.ShouldBe(ListenPortValidationStatus.Skipped);
        outcome.SkipReason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Validate_AddressesUnparseable_SkipsValidation()
    {
        // Defensive: if every address fails to parse as a Uri (would be an internal Kestrel
        // shape change), prefer "skipped" over a false-positive mismatch.
        var outcome = ListenPortValidator.Validate
        (
            configuredListenPort: 58400,
            listeningAddresses: ["not-a-url", string.Empty]
        );

        outcome.Status.ShouldBe(ListenPortValidationStatus.Skipped);
    }

    [Fact]
    public void Validate_AddressesMixedParseableAndJunk_UsesParseableOnly()
    {
        // One garbage address shouldn't poison the whole comparison if a real one is also
        // present.
        var outcome = ListenPortValidator.Validate
        (
            configuredListenPort: 58400,
            listeningAddresses: ["not-a-url", "http://localhost:58400"]
        );

        outcome.Status.ShouldBe(ListenPortValidationStatus.Match);
    }
}
