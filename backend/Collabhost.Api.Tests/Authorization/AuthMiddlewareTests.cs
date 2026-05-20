using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Authorization;
using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Authorization;

[Collection("Api")]
public class AuthMiddlewareTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task ConfigKey_Bypass_AuthenticatesAsAdmin()
    {
        // The config key (AdminKey) always works via bypass — auth/me should return administrator
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var me = JsonDocument.Parse(body).RootElement;

        me.GetProperty("role").GetString().ShouldBe("administrator");
    }

    [Fact]
    public async Task ValidDbUser_Authenticated()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // Create a DB user
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users");
        createRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        createRequest.Content = JsonContent.Create
        (
            new { name = $"Auth Test {suffix}", role = (int)UserRole.Agent },
            options: _jsonOptions
        );

        var createResponse = await _client.SendAsync(createRequest);

        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var createBody = await createResponse.Content.ReadAsStringAsync();
        var created = JsonDocument.Parse(createBody).RootElement;
        var agentKey = created.GetProperty("authKey").GetString()!;

        // Use the agent key on a public endpoint (apps list — accessible to agents)
        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/apps");
        listRequest.Headers.Add("X-User-Key", agentKey);

        var listResponse = await _client.SendAsync(listRequest);

        listResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeactivatedUser_Rejected_Returns401()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // Create and immediately deactivate a user
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users");
        createRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        createRequest.Content = JsonContent.Create
        (
            new { name = $"Deactivated {suffix}", role = (int)UserRole.Agent },
            options: _jsonOptions
        );

        var createResponse = await _client.SendAsync(createRequest);
        var createBody = await createResponse.Content.ReadAsStringAsync();
        var created = JsonDocument.Parse(createBody).RootElement;
        var userId = created.GetProperty("id").GetString();
        var agentKey = created.GetProperty("authKey").GetString()!;

        using var deactivateRequest = new HttpRequestMessage
        (
            HttpMethod.Patch, $"/api/v1/users/{userId}/deactivate"
        );
        deactivateRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        await _client.SendAsync(deactivateRequest);

        // Deactivated user should be rejected
        using var apiRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/apps");
        apiRequest.Headers.Add("X-User-Key", agentKey);

        var apiResponse = await _client.SendAsync(apiRequest);

        apiResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MissingKey_Returns401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/apps");

        // No X-User-Key header
        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InvalidKey_Returns401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/apps");
        request.Headers.Add("X-User-Key", "01NOTAREALKEY00000000000X");

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task McpPath_IsSkippedByMiddleware_ReachesMcpHandler()
    {
        // /mcp is in the REST auth middleware's skip list. Under the per-call auth model
        // (Card #332), the MCP server itself accepts unauthenticated session-level requests
        // (initialize, tools/list); per-call authKey enforcement happens inside each tool
        // body. So an unauthenticated `initialize` should reach the MCP handler and get a
        // valid MCP response, NOT a middleware 401.
        //
        // The key assertion: our REST auth middleware did NOT short-circuit before reaching
        // the MCP layer. Anything other than a middleware-style 401 with the middleware's
        // body shape proves that.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");

        // Send the minimum valid JSON-RPC body MCP expects. Accept header advertises both
        // application/json and text/event-stream so MCP picks the JSON streaming path.
        request.Content = new StringContent
        (
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}""",
            System.Text.Encoding.UTF8,
            "application/json"
        );
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");

        var response = await _client.SendAsync(request);

        // If our middleware short-circuited, the response body would carry the middleware's
        // exact shape: {"error":"Unauthorized","message":"API key is required. Provide
        // X-User-Key header."}. Any other response shape proves the middleware skip worked.
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldNotContain
        (
            "API key is required. Provide X-User-Key header.",
            Case.Sensitive,
            "If this string appears in the response body, the REST auth middleware short-circuited "
            + "the /mcp path -- meaning the skip-prefix entry for /mcp is broken and the MCP "
            + "transport never saw the request."
        );
    }

    [Fact]
    public async Task RequireRoleFilter_AdminAccessingUsersEndpoint_Returns200()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RequireRoleFilter_AgentAccessingUsersEndpoint_Returns403()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // Create an agent-role user
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users");
        createRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        createRequest.Content = JsonContent.Create
        (
            new { name = $"Agent 403 Test {suffix}", role = (int)UserRole.Agent },
            options: _jsonOptions
        );

        var createResponse = await _client.SendAsync(createRequest);

        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var createBody = await createResponse.Content.ReadAsStringAsync();
        var created = JsonDocument.Parse(createBody).RootElement;
        var agentKey = created.GetProperty("authKey").GetString()!;

        // Agent accessing admin-only endpoint should get 403
        using var usersRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users");
        usersRequest.Headers.Add("X-User-Key", agentKey);

        var usersResponse = await _client.SendAsync(usersRequest);

        usersResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var body = await usersResponse.Content.ReadAsStringAsync();
        var error = JsonDocument.Parse(body).RootElement;

        error.GetProperty("error").GetString().ShouldBe("Forbidden");
    }
}
