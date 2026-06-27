using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Authorization;
using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Authorization;

// Read-only control-plane key tier (#417). The read-only tier (UserRole.ReadOnly) sits below
// Agent: it answers "what exists and is it running" for status pages, external monitors, and
// watcher automation, without holding start/kill authority or secret visibility.
//
// These drive the LIVE REST surface with a freshly-minted read-only key (the wiring itself, which
// a static read would miss). The MCP-surface authority for the same tier is proven directly in
// EntitlementsTests; together they cover both surfaces. Each test states the property it proves.
[Collection("Api")]
public class ReadOnlyKeyTierTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Property: a read-only key authenticates and its role persists + round-trips through the API
    // as "readonly" -- confirming the new enum value is storable and serializes as expected.
    [Fact]
    public async Task ReadOnlyKey_AuthMe_ReportsReadOnlyRole()
    {
        var key = await MintReadOnlyKeyAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Add("X-User-Key", key);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var role = JsonDocument.Parse(body).RootElement.GetProperty("role").GetString();

        role.ShouldBe("readonly");
    }

    // Property: the read-only tier is ACCEPTED on the apps list read (the "are all apps running"
    // observability call that drove this card).
    [Fact]
    public async Task ReadOnlyKey_ListApps_Returns200()
    {
        var key = await MintReadOnlyKeyAsync();

        var status = await GetStatusAsync("/api/v1/apps", key);

        status.ShouldBe(HttpStatusCode.OK);
    }

    // Property: the read-only tier is ACCEPTED on the app-detail read. A nonexistent slug reaches
    // the handler and returns 404 -- the point is it is NOT 401/403, so the read gate let it past.
    [Fact]
    public async Task ReadOnlyKey_GetApp_NotGatedOut()
    {
        var key = await MintReadOnlyKeyAsync();

        var status = await GetStatusAsync("/api/v1/apps/nonexistent", key);

        status.ShouldBe(HttpStatusCode.NotFound);
    }

    // Property: the read-only tier CANNOT mutate the control plane. Both Agent-gated mutations
    // (start/stop/restart/kill/register/update_settings/reload) and the Administrator-only
    // mutation (delete) are refused -- it falls out of RequireRoleFilter's default-deny for any
    // role that is neither Agent nor Administrator, with zero per-route gate logic added.
    [Theory]
    [InlineData("POST", "/api/v1/apps/nonexistent/start")]
    [InlineData("POST", "/api/v1/apps/nonexistent/stop")]
    [InlineData("POST", "/api/v1/apps/nonexistent/restart")]
    [InlineData("POST", "/api/v1/apps/nonexistent/kill")]
    [InlineData("POST", "/api/v1/apps")]
    [InlineData("PUT", "/api/v1/apps/nonexistent/settings")]
    [InlineData("POST", "/api/v1/proxy/reload")]
    [InlineData("DELETE", "/api/v1/apps/nonexistent")]
    public async Task ReadOnlyKey_Mutation_IsForbidden(string method, string path)
    {
        var key = await MintReadOnlyKeyAsync();

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("X-User-Key", key);
        // An empty JSON body so body-binding endpoints (register, settings) bind cleanly and the
        // 403 is unambiguously the role gate, not a 400 from a missing body. Bodyless endpoints
        // ignore it.
        request.Content = JsonContent.Create(new { }, options: _jsonOptions);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // Property: the read-only tier is DENIED reads that can surface secrets (logs, log stream,
    // settings) or operational history (activity events, dashboard event feed). This is the real
    // delta over the Agent role, which is granted all of these.
    [Theory]
    [InlineData("/api/v1/apps/nonexistent/logs")]
    [InlineData("/api/v1/apps/nonexistent/logs/stream")]
    [InlineData("/api/v1/apps/nonexistent/settings")]
    [InlineData("/api/v1/events")]
    [InlineData("/api/v1/dashboard/events")]
    public async Task ReadOnlyKey_SecretBearingRead_IsForbidden(string path)
    {
        var key = await MintReadOnlyKeyAsync();

        var status = await GetStatusAsync(path, key);

        status.ShouldBe(HttpStatusCode.Forbidden);
    }

    // Property: gating those reads to Agent did not over-restrict the existing Agent tier. An
    // Agent key still passes the gate (200, or 404 for the nonexistent slug) -- so Agent consumers
    // of logs/settings/events are not regressed by the introduction of the read-only tier. (The
    // log stream is omitted here: passing its gate opens a live SSE stream, which is verified
    // elsewhere; the read-only refusal above already proves its gate is wired.)
    [Theory]
    [InlineData("/api/v1/apps/nonexistent/logs")]
    [InlineData("/api/v1/apps/nonexistent/settings")]
    [InlineData("/api/v1/events")]
    [InlineData("/api/v1/dashboard/events")]
    public async Task AgentKey_NewlyGatedRead_IsNotForbidden(string path)
    {
        var key = await MintAgentKeyAsync();

        var status = await GetStatusAsync(path, key);

        status.ShouldNotBe(HttpStatusCode.Forbidden);
    }

    private async Task<HttpStatusCode> GetStatusAsync(string path, string userKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-User-Key", userKey);

        var response = await _client.SendAsync(request);

        return response.StatusCode;
    }

    private Task<string> MintReadOnlyKeyAsync() => MintKeyAsync(UserRole.ReadOnly);

    private Task<string> MintAgentKeyAsync() => MintKeyAsync(UserRole.Agent);

    private async Task<string> MintKeyAsync(UserRole role)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        request.Content = JsonContent.Create
        (
            new { name = $"{role} Tier {suffix}", role = (int)role },
            options: _jsonOptions
        );

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var body = await response.Content.ReadAsStringAsync();
        var authKey = JsonDocument.Parse(body).RootElement.GetProperty("authKey").GetString();

        authKey.ShouldNotBeNullOrEmpty();

        return authKey!;
    }
}
