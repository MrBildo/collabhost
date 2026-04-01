using System.Net;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

using static Collabhost.Api.Tests.Fixtures.AppTestHelpers;

namespace Collabhost.Api.Tests;

public class DeleteProtectionTests(CollabhostApiFixture fixture) : IClassFixture<CollabhostApiFixture>
{
    private readonly CollabhostApiFixture _fixture = fixture;

    [Fact]
    public async Task Delete_AllowsRegularAppType()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "delete-regular");

        // Act
        var response = await client.DeleteAsync($"/api/v1/apps/{externalId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Verify the app is actually gone
        var getResponse = await client.GetAsync($"/api/v1/apps/{externalId}");
        getResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

}
