using System.Net;
using Ingest.IntegrationTests.Fixtures;

namespace Ingest.IntegrationTests;

/// <summary>Capability enforcement: a Service-role key cannot reach the admin surface, while the
/// admin key can.</summary>
public sealed class CapabilityTests : IntegrationTestBase
{
    public CapabilityTests(IngestAppFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Service_key_is_forbidden_on_admin_endpoints()
    {
        var (_, apiKey, _) = await CreateServiceAccountAsync();
        using var service = Fixture.CreateClient(apiKey);

        var response = await service.GetAsync("/api/admin/schemas");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_key_is_allowed_on_admin_endpoints()
    {
        var response = await Admin.GetAsync("/api/admin/schemas");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
