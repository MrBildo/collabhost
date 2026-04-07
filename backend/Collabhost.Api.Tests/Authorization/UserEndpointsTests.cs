using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Authorization;
using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Authorization;

[Collection("Api")]
public class UserEndpointsTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task CreateUser_ValidRequest_Returns201WithAuthKey()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        request.Content = JsonContent.Create
        (
            new { name = $"Test User {suffix}", role = (int)UserRole.Agent },
            options: _jsonOptions
        );

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var body = await response.Content.ReadAsStringAsync();
        var user = JsonDocument.Parse(body).RootElement;

        user.GetProperty("name").GetString().ShouldBe($"Test User {suffix}");
        user.GetProperty("role").GetString().ShouldBe("agent");
        user.GetProperty("isActive").GetBoolean().ShouldBeTrue();

        // Create returns authKey
        user.TryGetProperty("authKey", out var authKeyProp).ShouldBeTrue();
        authKeyProp.GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task ListUsers_DoesNotIncludeAuthKey()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var users = JsonDocument.Parse(body).RootElement;

        users.GetArrayLength().ShouldBeGreaterThan(0);

        // authKey must not appear on any user in the list
        foreach (var user in users.EnumerateArray())
        {
            user.TryGetProperty("authKey", out _).ShouldBeFalse("List response must not include authKey");
        }
    }

    [Fact]
    public async Task GetUserById_ValidId_ReturnsUserWithoutAuthKey()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // Create a user to look up
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users");
        createRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        createRequest.Content = JsonContent.Create
        (
            new { name = $"Lookup User {suffix}", role = (int)UserRole.Agent },
            options: _jsonOptions
        );

        var createResponse = await _client.SendAsync(createRequest);

        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var createBody = await createResponse.Content.ReadAsStringAsync();
        var created = JsonDocument.Parse(createBody).RootElement;
        var userId = created.GetProperty("id").GetString();

        // Get by id
        using var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/users/{userId}");
        getRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var getResponse = await _client.SendAsync(getRequest);

        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var getBody = await getResponse.Content.ReadAsStringAsync();
        var user = JsonDocument.Parse(getBody).RootElement;

        user.GetProperty("id").GetString().ShouldBe(userId);
        user.GetProperty("name").GetString().ShouldBe($"Lookup User {suffix}");
        user.TryGetProperty("authKey", out _).ShouldBeFalse("Get response must not include authKey");
    }

    [Fact]
    public async Task GetUserById_InvalidId_Returns404()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users/01NOTAVALIDULIDSTRING00000");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeactivateUser_ValidId_ReturnsDeactivatedUser()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // Create a user to deactivate
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users");
        createRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        createRequest.Content = JsonContent.Create
        (
            new { name = $"Deactivate Me {suffix}", role = (int)UserRole.Agent },
            options: _jsonOptions
        );

        var createResponse = await _client.SendAsync(createRequest);

        var createBody = await createResponse.Content.ReadAsStringAsync();
        var created = JsonDocument.Parse(createBody).RootElement;
        var userId = created.GetProperty("id").GetString();

        // Deactivate
        using var deactivateRequest = new HttpRequestMessage
        (
            HttpMethod.Patch, $"/api/v1/users/{userId}/deactivate"
        );
        deactivateRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var deactivateResponse = await _client.SendAsync(deactivateRequest);

        deactivateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var deactivateBody = await deactivateResponse.Content.ReadAsStringAsync();
        var deactivated = JsonDocument.Parse(deactivateBody).RootElement;

        deactivated.GetProperty("id").GetString().ShouldBe(userId);
        deactivated.GetProperty("isActive").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task GetMe_AdminKey_ReturnsCurrentUser()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var me = JsonDocument.Parse(body).RootElement;

        me.GetProperty("role").GetString().ShouldBe("administrator");
        me.GetProperty("id").GetString().ShouldNotBeNullOrEmpty();
        me.GetProperty("name").GetString().ShouldNotBeNullOrEmpty();
    }
}
