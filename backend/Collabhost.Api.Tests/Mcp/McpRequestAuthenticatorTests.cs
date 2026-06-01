using Collabhost.Api.Authorization;
using Collabhost.Api.Mcp;
using Collabhost.Api.Tests.Fixtures;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Mcp;

// Regression coverage for the MCP silent-credential-fallback defect (Card #371).
//
// Background (settled by source-read during #371 impl):
// McpRequestAuthenticator is the single chokepoint every MCP tool calls. It resolves the
// effective key from the per-call `authKey` argument, falling back to the X-User-Key header
// captured at session setup (McpHeaderFallback). Before #371 the fallback was silent in two
// ways: (1) when a per-call key and a session header resolved to *different* users, the
// per-call key silently won (mis-attribution -- #370), and (2) a call that authenticated
// purely off the pinned header left no trace that the fallback was used.
//
// What this also pins: the no-credential path. The card's escalation hypothesis was "an
// unidentified MCP call is silently granted a default (first-admin) identity." The source
// shows that is NOT the behavior -- with neither a per-call authKey nor a session header,
// AuthenticateAsync returns an error result and never seeds CurrentUser. That guard is
// asserted here so the property cannot regress into a default-identity grant.
//
// Fixture choice: these tests exercise the real McpRequestAuthenticator resolved from the
// production DI container against the real user store and AuthKeyResolver -- the same seam the
// tool bodies hit. CurrentUser and McpHeaderFallback are scoped, so a single test-scoped scope
// gives the authenticator and the assertions a shared view of the seeded state.
[Collection("Api")]
public class McpRequestAuthenticatorTests(ApiFixture fixture)
{
    private readonly IServiceProvider _services = fixture.Services;

    private const string _toolName = "list_apps";

    [Fact]
    public async Task AuthenticateAsync_NoCallKeyAndNoHeader_ReturnsErrorAndDoesNotSeedUser()
    {
        await using var scope = _services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var authenticator = sp.GetRequiredService<McpRequestAuthenticator>();
        var currentUser = sp.GetRequiredService<CurrentUser>();

        // No header set on McpHeaderFallback, no per-call authKey.
        var result = await authenticator.AuthenticateAsync(null, _toolName, CancellationToken.None);

        result.ShouldNotBeNull();
        result.IsError.ShouldBe(true);

        // The reconciliation assertion: an unidentified call resolves to no identity at all --
        // it is NOT silently granted the first-seeded admin (or any) identity.
        Should.Throw<InvalidOperationException>(() => currentUser.User);
    }

    [Fact]
    public async Task AuthenticateAsync_PerCallKeyOnly_AuthenticatesAndSeedsUser()
    {
        await using var scope = _services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var authenticator = sp.GetRequiredService<McpRequestAuthenticator>();
        var currentUser = sp.GetRequiredService<CurrentUser>();

        var result = await authenticator.AuthenticateAsync(ApiFixture.AdminKey, _toolName, CancellationToken.None);

        result.ShouldBeNull();
        currentUser.User.AuthKey.ShouldBe(ApiFixture.AdminKey);
    }

    [Fact]
    public async Task AuthenticateAsync_HeaderFallbackOnly_AuthenticatesAndSeedsUser()
    {
        await using var scope = _services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        // Simulate a v1.0.x client that pinned X-User-Key at connection time.
        sp.GetRequiredService<McpHeaderFallback>().Set(ApiFixture.AdminKey);

        var authenticator = sp.GetRequiredService<McpRequestAuthenticator>();
        var currentUser = sp.GetRequiredService<CurrentUser>();

        var result = await authenticator.AuthenticateAsync(null, _toolName, CancellationToken.None);

        // Backward-compat contract: the header fallback still authenticates. (The fix logs a
        // warning on this path; the contract is unchanged.)
        result.ShouldBeNull();
        currentUser.User.AuthKey.ShouldBe(ApiFixture.AdminKey);
    }

    [Fact]
    public async Task AuthenticateAsync_SameKeyAsCallAndHeader_AuthenticatesWithoutConflict()
    {
        await using var scope = _services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        // Same key string on both channels cannot disagree -- not a conflict.
        sp.GetRequiredService<McpHeaderFallback>().Set(ApiFixture.AdminKey);

        var authenticator = sp.GetRequiredService<McpRequestAuthenticator>();
        var currentUser = sp.GetRequiredService<CurrentUser>();

        var result = await authenticator.AuthenticateAsync(ApiFixture.AdminKey, _toolName, CancellationToken.None);

        result.ShouldBeNull();
        currentUser.User.AuthKey.ShouldBe(ApiFixture.AdminKey);
    }

    [Fact]
    public async Task AuthenticateAsync_ConflictingCredentials_RefusesAndDoesNotSeedUser()
    {
        var ct = CancellationToken.None;

        // Create a second, distinct user so the per-call key and the header key resolve to
        // different User identities.
        var agent = await _services.GetRequiredService<UserStore>()
            .CreateAsync("Conflict Agent", UserRole.Agent, ct);

        await using var scope = _services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        // Per-call key = admin; session header = a different user (the agent).
        sp.GetRequiredService<McpHeaderFallback>().Set(agent.AuthKey);

        var authenticator = sp.GetRequiredService<McpRequestAuthenticator>();
        var currentUser = sp.GetRequiredService<CurrentUser>();

        var result = await authenticator.AuthenticateAsync(ApiFixture.AdminKey, _toolName, ct);

        // Fail-loud (#371): the authenticator refuses rather than silently letting one win.
        result.ShouldNotBeNull();
        result.IsError.ShouldBe(true);

        // No identity is seeded for a refused, ambiguous call.
        Should.Throw<InvalidOperationException>(() => currentUser.User);
    }
}
