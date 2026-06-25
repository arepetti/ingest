using System.Net;
using Ingest.Api.Models;
using Ingest.IntegrationTests.Fixtures;

namespace Ingest.IntegrationTests;

/// <summary>
/// Deleting an API key (as distinct from revoking it): an admin can permanently remove a key
/// whether it is still active or already revoked, and removing an unknown key is a 404.
/// </summary>
public sealed class ApiKeyDeleteTests : IntegrationTestBase
{
    public ApiKeyDeleteTests(IngestAppFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Active_key_can_be_deleted_and_disappears_from_the_listing()
    {
        var (accountId, _, _) = await CreateServiceAccountAsync();
        var created = await (await Admin.PostJsonAsync($"/api/admin/accounts/{accountId}/keys", new { })).ReadAsync<GeneratedApiKeyResponse>();

        var response = await Admin.DeleteAsync($"/api/admin/accounts/{accountId}/keys/{created.Key.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var list = await (await Admin.GetAsync($"/api/admin/accounts/{accountId}/keys")).ReadAsync<List<ApiKeyDto>>();
        Assert.DoesNotContain(list, k => k.Id == created.Key.Id);
    }

    [Fact]
    public async Task Already_revoked_key_can_be_deleted()
    {
        var (accountId, _, _) = await CreateServiceAccountAsync();
        var created = await (await Admin.PostJsonAsync($"/api/admin/accounts/{accountId}/keys", new { })).ReadAsync<GeneratedApiKeyResponse>();

        var revoke = await Admin.PostAsync($"/api/admin/accounts/{accountId}/keys/{created.Key.Id}/revoke", null);
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);

        var response = await Admin.DeleteAsync($"/api/admin/accounts/{accountId}/keys/{created.Key.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var list = await (await Admin.GetAsync($"/api/admin/accounts/{accountId}/keys")).ReadAsync<List<ApiKeyDto>>();
        Assert.DoesNotContain(list, k => k.Id == created.Key.Id);
    }

    [Fact]
    public async Task Deleting_an_unknown_key_is_404()
    {
        var (accountId, _, _) = await CreateServiceAccountAsync();
        var response = await Admin.DeleteAsync($"/api/admin/accounts/{accountId}/keys/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
