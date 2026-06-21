using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Authorization;
using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Authorization;

// Cross-surface role parity (SEC-01 / #418).
//
// The property under guard: for every control-plane operation exposed on BOTH the REST surface
// and the MCP surface, the two surfaces require the SAME role. The SEC-01 bypass was exactly a
// violation of this -- MCP gated delete_app to Administrator while DELETE /api/v1/apps/{slug}
// accepted the same Agent key. This suite goes RED if a future change re-opens such a disagreement
// on either side (drop a REST RequireRoleFilter, or add/remove a tool from Entitlements._agentTools).
//
// It is deliberately cross-surface, not single-surface: it drives the LIVE REST gate behaviorally
// (Agent key -> 403-or-not, the wiring itself, which a static metadata read would miss) AND reads
// the LIVE MCP authority (Entitlements.CanAccessTool). The canonical table below is the ratified
// role-per-operation model made executable.
[Collection("Api")]
public class CrossSurfaceRoleParityTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // The ratified control-plane role model (#418, Bill S96). Each row pins, for one operation:
    // the REST request that reaches it, its MCP tool twin (null when REST-only), and the role the
    // operation requires. Agent+ means Agent or Administrator; Administrator means Administrator only.
    public static TheoryData<ControlPlaneOperation> Operations() =>
    [
        new ControlPlaneOperation("register_app", HttpMethod.Post, "/api/v1/apps", "register_app", UserRole.Agent),
        new ControlPlaneOperation("delete_app", HttpMethod.Delete, "/api/v1/apps/nonexistent", "delete_app", UserRole.Administrator),
        new ControlPlaneOperation("update_settings", HttpMethod.Put, "/api/v1/apps/nonexistent/settings", "update_settings", UserRole.Agent),
        new ControlPlaneOperation("start_app", HttpMethod.Post, "/api/v1/apps/nonexistent/start", "start_app", UserRole.Agent),
        new ControlPlaneOperation("stop_app", HttpMethod.Post, "/api/v1/apps/nonexistent/stop", "stop_app", UserRole.Agent),
        new ControlPlaneOperation("restart_app", HttpMethod.Post, "/api/v1/apps/nonexistent/restart", "restart_app", UserRole.Agent),
        new ControlPlaneOperation("kill_app", HttpMethod.Post, "/api/v1/apps/nonexistent/kill", "kill_app", UserRole.Agent),
        new ControlPlaneOperation("reload_proxy", HttpMethod.Post, "/api/v1/proxy/reload", "reload_proxy", UserRole.Agent),
        new ControlPlaneOperation("browse_filesystem", HttpMethod.Get, "/api/v1/filesystem/browse", "browse_filesystem", UserRole.Agent),
        new ControlPlaneOperation("detect_strategy", HttpMethod.Get, "/api/v1/filesystem/detect-strategy", "detect_strategy", UserRole.Agent),
        // REST-only: the runtime-config-file import preview reveals on-disk config (incl. secrets)
        // and has no MCP twin (confirmed against the MCP tool surface). Administrator-gated; it
        // participates in the REST gate check but not the cross-surface equality.
        new ControlPlaneOperation("runtime_config_file_import", HttpMethod.Post, "/api/v1/apps/nonexistent/runtime-config-file/import", McpTool: null, UserRole.Administrator),
    ];

    // The cross-surface guard: for every operation with an MCP twin, "is an Agent allowed?" must be
    // identical on both surfaces. REST authority is read behaviorally (does an Agent key get past the
    // gate?); MCP authority is read from Entitlements.CanAccessTool. A disagreement is the SEC-01 bug.
    [Theory]
    [MemberData(nameof(Operations))]
    public async Task ControlPlaneOperation_RestAndMcpAgreeOnRequiredRole(ControlPlaneOperation operation)
    {
        var agentAllowedOnRest = await AgentPassesRestGateAsync(operation);

        // REST gate matches the ratified role: Agent allowed iff the operation is Agent-tier.
        agentAllowedOnRest.ShouldBe
        (
            operation.RequiredRole == UserRole.Agent,
            $"REST {operation.Method} {operation.RestPath} did not gate '{operation.Name}' to {operation.RequiredRole}."
        );

        if (operation.McpTool is null)
        {
            return;
        }

        var agentAllowedOnMcp = Entitlements.CanAccessTool(UserRole.Agent, operation.McpTool);

        // The actual cross-surface property: the two authority lists must agree for this operation.
        agentAllowedOnMcp.ShouldBe
        (
            agentAllowedOnRest,
            $"Surface disagreement on '{operation.Name}': REST allows Agent = {agentAllowedOnRest}, "
            + $"MCP allows Agent = {agentAllowedOnMcp}. Both surfaces must require the same role."
        );
    }

    // The SEC-01 bypass, stated directly and named: DELETE /api/v1/apps/{slug} with an Agent key
    // returned not-403 before the fix (it deleted the app) while MCP delete_app already refused the
    // same key. This is the exact hole the card was filed for; it stays as a focused regression
    // alongside the table-driven guard so the security invariant is legible even if the table is
    // refactored later.
    [Fact]
    public async Task DeleteApp_WithAgentKey_IsForbidden()
    {
        var agentKey = await MintAgentKeyAsync();

        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/apps/nonexistent");
        request.Headers.Add("X-User-Key", agentKey);

        var response = await _client.SendAsync(request);

        // 403 from the role filter -- which runs before the handler, so the app never needs to exist.
        // The pre-fix behavior reached the handler (404 for a missing app, 204 for a real one).
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // An Administrator key must reach every control-plane operation on REST (Administrator >= every
    // role). This is the other half of the gate: the filter must not over-restrict.
    [Theory]
    [MemberData(nameof(Operations))]
    public async Task ControlPlaneOperation_AdministratorIsNeverGatedOut(ControlPlaneOperation operation)
    {
        using var request = BuildRequest(operation, ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldNotBe
        (
            HttpStatusCode.Forbidden,
            $"Administrator was 403'd on {operation.Method} {operation.RestPath} ('{operation.Name}')."
        );
    }

    // Drives the LIVE REST role gate with a freshly-minted Agent key. The gate runs before the
    // handler, so a 403 is unambiguously the role filter (no app needs to exist); any other status
    // means the Agent got past the gate into the handler.
    //
    // When the response IS a 403, we confirm it came from RequireRoleFilter and not from any handler
    // that also emits 403 (e.g. FilesystemEndpoints returns 403 on UnauthorizedAccessException for a
    // real access-denied directory). RequireRoleFilter emits a plain JSON object with a 'message'
    // field containing "This endpoint requires the <role> role."; handler 403s use TypedResults.Problem
    // with a 'detail' field and a different message shape. Asserting the body carries the filter's
    // message keeps the "403 == role gate" premise explicit and catches any future test-row edit that
    // accidentally exercises a handler 403 instead.
    private async Task<bool> AgentPassesRestGateAsync(ControlPlaneOperation operation)
    {
        var agentKey = await MintAgentKeyAsync();

        using var request = BuildRequest(operation, agentKey);

        var response = await _client.SendAsync(request);

        if (response.StatusCode != HttpStatusCode.Forbidden)
        {
            return true;
        }

        // Confirm the 403 is the role-gate rejection, not a handler-emitted 403.
        // RequireRoleFilter emits: { "error": "Forbidden", "message": "This endpoint requires the <role> role." }
        // Handler 403s (e.g. FilesystemEndpoints on UnauthorizedAccessException) use TypedResults.Problem
        // with a different message shape and no "requires the" fragment at the root.
        var body = await response.Content.ReadAsStringAsync();
        var expectedFragment = "This endpoint requires the";

        body.ShouldContain(expectedFragment);

        return false;
    }

    private static HttpRequestMessage BuildRequest(ControlPlaneOperation operation, string userKey)
    {
        var request = new HttpRequestMessage(operation.Method, operation.RestPath);
        request.Headers.Add("X-User-Key", userKey);

        return request;
    }

    private async Task<string> MintAgentKeyAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        request.Content = JsonContent.Create
        (
            new { name = $"Parity Agent {suffix}", role = (int)UserRole.Agent },
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

// One row of the ratified role-per-operation table: a control-plane operation, the REST request
// shape that reaches it, its MCP tool twin (null = REST-only, no twin), and the role it requires.
public sealed record ControlPlaneOperation
(
    string Name,
    HttpMethod Method,
    string RestPath,
    string? McpTool,
    UserRole RequiredRole
)
{
    public override string ToString() => Name;
}
