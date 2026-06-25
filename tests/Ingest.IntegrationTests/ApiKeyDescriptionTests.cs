using System.Net;
using Ingest.Api.Models;
using Ingest.IntegrationTests.Fixtures;

namespace Ingest.IntegrationTests;

/// <summary>
/// The optional free-form description on an API key: it round-trips through create, shows up in the
/// key listing, can be edited afterwards, and is length-validated.
/// </summary>
public sealed class ApiKeyDescriptionTests : IntegrationTestBase
{
    public ApiKeyDescriptionTests(IngestAppFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Created_key_keeps_its_description_and_lists_it()
    {
        var (accountId, _, _) = await CreateServiceAccountAsync();

        var created = await (await Admin.PostJsonAsync($"/api/admin/accounts/{accountId}/keys", new
        {
            description = "  holiday cover for Jane  ",
        })).ReadAsync<GeneratedApiKeyResponse>();

        // Trimmed on the way in.
        Assert.Equal("holiday cover for Jane", created.Key.Description);

        var list = await (await Admin.GetAsync($"/api/admin/accounts/{accountId}/keys")).ReadAsync<List<ApiKeyDto>>();
        Assert.Contains(list, k => k.Id == created.Key.Id && k.Description == "holiday cover for Jane");
    }

    [Fact]
    public async Task Description_can_be_edited_after_creation()
    {
        var (accountId, _, _) = await CreateServiceAccountAsync();
        var created = await (await Admin.PostJsonAsync($"/api/admin/accounts/{accountId}/keys", new { })).ReadAsync<GeneratedApiKeyResponse>();
        Assert.Null(created.Key.Description);

        var updated = await (await Admin.PutJsonAsync($"/api/admin/accounts/{accountId}/keys/{created.Key.Id}", new
        {
            description = "Power BI prod refresh",
        })).ReadAsync<ApiKeyDto>();

        Assert.Equal("Power BI prod refresh", updated.Description);

        // And clearing it works.
        var cleared = await (await Admin.PutJsonAsync($"/api/admin/accounts/{accountId}/keys/{created.Key.Id}", new
        {
            description = "",
        })).ReadAsync<ApiKeyDto>();
        Assert.Null(cleared.Description);
    }

    [Fact]
    public async Task Updating_an_unknown_key_is_404()
    {
        var (accountId, _, _) = await CreateServiceAccountAsync();
        var response = await Admin.PutJsonAsync($"/api/admin/accounts/{accountId}/keys/{Guid.NewGuid()}", new { description = "x" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Overlong_description_is_rejected()
    {
        var (accountId, _, _) = await CreateServiceAccountAsync();
        var response = await Admin.PostJsonAsync($"/api/admin/accounts/{accountId}/keys", new
        {
            description = new string('x', 201),
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
