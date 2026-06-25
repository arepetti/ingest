using System.Net;
using Ingest.Api.Models;
using Ingest.IntegrationTests.Fixtures;

namespace Ingest.IntegrationTests;

/// <summary>
/// Per-service operator scoping: an Operator carrying an <c>AssignedServiceIds</c> allowlist only
/// ever sees data belonging to those services, across every cross-service surface (the OData feed,
/// the admin submissions list, single-submission lookup, and service-targeting writes). An Operator
/// with an empty allowlist is unrestricted (sees everything, exactly as before), and an Admin always
/// sees everything even if an allowlist is somehow stored against them.
/// </summary>
public sealed class OperatorScopeTests : IntegrationTestBase
{
    public OperatorScopeTests(IngestAppFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Scoped_operator_sees_only_assigned_service_in_odata_feed()
    {
        var schema = await CreateSchemaAsync();
        var (svc1Id, key1, _) = await CreateServiceAccountAsync();
        var (svc2Id, key2, _) = await CreateServiceAccountAsync();
        using (var s1 = Fixture.CreateClient(key1)) await SubmitSampleAsync(s1, schema.Name, 11);
        using (var s2 = Fixture.CreateClient(key2)) await SubmitSampleAsync(s2, schema.Name, 22);

        // Operator confined to service 1 only.
        var (_, opKey, _) = await CreateOperatorAsync(new[] { svc1Id });
        using var op = Fixture.CreateClient(opKey);

        var json = await (await op.GetAsync($"/odata/samples?$filter=SchemaName eq '{schema.Name}'")).ReadJsonAsync();
        var rows = json.GetProperty("value");

        Assert.Equal(1, rows.GetArrayLength());
        Assert.Equal(svc1Id, rows[0].GetProperty("ServiceAccountId").GetGuid());
        Assert.NotEqual(svc2Id, rows[0].GetProperty("ServiceAccountId").GetGuid());
    }

    [Fact]
    public async Task Unscoped_operator_sees_every_service_in_odata_feed()
    {
        var schema = await CreateSchemaAsync();
        var (_, key1, _) = await CreateServiceAccountAsync();
        var (_, key2, _) = await CreateServiceAccountAsync();
        using (var s1 = Fixture.CreateClient(key1)) await SubmitSampleAsync(s1, schema.Name, 11);
        using (var s2 = Fixture.CreateClient(key2)) await SubmitSampleAsync(s2, schema.Name, 22);

        // No allowlist => unrestricted.
        var (_, opKey, _) = await CreateOperatorAsync();
        using var op = Fixture.CreateClient(opKey);

        var json = await (await op.GetAsync($"/odata/samples?$filter=SchemaName eq '{schema.Name}'")).ReadJsonAsync();
        Assert.Equal(2, json.GetProperty("value").GetArrayLength());
    }

    [Fact]
    public async Task Scoped_operator_submission_list_is_confined_to_assigned_services()
    {
        var schema = await CreateSchemaAsync();
        var (svc1Id, key1, _) = await CreateServiceAccountAsync();
        var (svc2Id, key2, _) = await CreateServiceAccountAsync();
        using (var s1 = Fixture.CreateClient(key1)) await SubmitSampleAsync(s1, schema.Name, 11);
        using (var s2 = Fixture.CreateClient(key2)) await SubmitSampleAsync(s2, schema.Name, 22);

        var (_, opKey, _) = await CreateOperatorAsync(new[] { svc1Id });
        using var op = Fixture.CreateClient(opKey);

        var page = await (await op.GetAsync($"/api/admin/submissions?schemaName={schema.Name}")).ReadAsync<PagedResponse<SubmissionDto>>();
        Assert.All(page.Items, s => Assert.Equal(svc1Id, s.ServiceAccountId));
        Assert.DoesNotContain(page.Items, s => s.ServiceAccountId == svc2Id);
    }

    [Fact]
    public async Task Scoped_operator_filtering_on_out_of_scope_service_sees_nothing()
    {
        var schema = await CreateSchemaAsync();
        var (svc1Id, key1, _) = await CreateServiceAccountAsync();
        var (svc2Id, key2, _) = await CreateServiceAccountAsync();
        using (var s1 = Fixture.CreateClient(key1)) await SubmitSampleAsync(s1, schema.Name, 11);
        using (var s2 = Fixture.CreateClient(key2)) await SubmitSampleAsync(s2, schema.Name, 22);

        var (_, opKey, _) = await CreateOperatorAsync(new[] { svc1Id });
        using var op = Fixture.CreateClient(opKey);

        // Explicitly asking for the *other* service yields an empty page, never that service's data.
        var page = await (await op.GetAsync($"/api/admin/submissions?serviceId={svc2Id}")).ReadAsync<PagedResponse<SubmissionDto>>();
        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
    }

    [Fact]
    public async Task Scoped_operator_gets_404_for_out_of_scope_submission_by_id()
    {
        var schema = await CreateSchemaAsync();
        var (svc1Id, _, _) = await CreateServiceAccountAsync();
        var (_, key2, _) = await CreateServiceAccountAsync();
        Guid otherSubmissionId;
        using (var s2 = Fixture.CreateClient(key2)) otherSubmissionId = await SubmitSampleAsync(s2, schema.Name, 22);

        var (_, opKey, _) = await CreateOperatorAsync(new[] { svc1Id });
        using var op = Fixture.CreateClient(opKey);

        var response = await op.GetAsync($"/api/admin/submissions/{otherSubmissionId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Scoped_operator_can_read_its_own_assigned_submission_by_id()
    {
        var schema = await CreateSchemaAsync();
        var (svc1Id, key1, _) = await CreateServiceAccountAsync();
        Guid ownSubmissionId;
        using (var s1 = Fixture.CreateClient(key1)) ownSubmissionId = await SubmitSampleAsync(s1, schema.Name, 11);

        var (_, opKey, _) = await CreateOperatorAsync(new[] { svc1Id });
        using var op = Fixture.CreateClient(opKey);

        var response = await op.GetAsync($"/api/admin/submissions/{ownSubmissionId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Admin_ignores_assigned_service_allowlist()
    {
        var schema = await CreateSchemaAsync();
        var (_, key1, _) = await CreateServiceAccountAsync();
        var (_, key2, _) = await CreateServiceAccountAsync();
        using (var s1 = Fixture.CreateClient(key1)) await SubmitSampleAsync(s1, schema.Name, 11);
        using (var s2 = Fixture.CreateClient(key2)) await SubmitSampleAsync(s2, schema.Name, 22);

        // The bootstrap admin has no allowlist and must see both rows.
        var json = await (await Admin.GetAsync($"/odata/samples?$filter=SchemaName eq '{schema.Name}'")).ReadJsonAsync();
        Assert.Equal(2, json.GetProperty("value").GetArrayLength());
    }

    [Fact]
    public async Task Creating_an_admin_with_an_allowlist_drops_it()
    {
        // Even if a scope is supplied, an Admin must come back unrestricted (empty allowlist).
        var (svcId, _, _) = await CreateServiceAccountAsync();
        var name = $"adm-{Unique()}";
        var account = await (await Admin.PostJsonAsync("/api/admin/accounts", new
        {
            name,
            label = name,
            email = $"{name}@example.com",
            kind = "User",
            role = "Admin",
            enabled = true,
            assignedServiceIds = new[] { svcId },
        })).ReadAsync<AccountDto>();

        Assert.Empty(account.AssignedServiceIds);
    }

    [Fact]
    public async Task Me_exposes_assigned_scope_for_scoped_operator()
    {
        var (svcId, _, _) = await CreateServiceAccountAsync();
        var (_, opKey, _) = await CreateOperatorAsync(new[] { svcId });
        using var op = Fixture.CreateClient(opKey);

        var me = await (await op.GetAsync("/api/me")).ReadJsonAsync();
        var scope = me.GetProperty("assignedServiceIds").EnumerateArray().Select(e => Guid.Parse(e.GetString()!)).ToList();

        Assert.Single(scope);
        Assert.Contains(svcId, scope);
    }

    [Fact]
    public async Task Me_reports_empty_scope_for_unrestricted_caller()
    {
        var (_, opKey, _) = await CreateOperatorAsync();
        using var op = Fixture.CreateClient(opKey);

        var me = await (await op.GetAsync("/api/me")).ReadJsonAsync();
        Assert.Empty(me.GetProperty("assignedServiceIds").EnumerateArray());

        // The admin is likewise unrestricted.
        var adminMe = await (await Admin.GetAsync("/api/me")).ReadJsonAsync();
        Assert.Empty(adminMe.GetProperty("assignedServiceIds").EnumerateArray());
    }

    [Fact]
    public async Task Scoped_operator_cannot_create_submission_for_out_of_scope_service()
    {
        var schema = await CreateSchemaAsync();
        var (svc1Id, _, _) = await CreateServiceAccountAsync();
        var (svc2Id, _, _) = await CreateServiceAccountAsync();

        var (_, opKey, _) = await CreateOperatorAsync(new[] { svc1Id });
        using var op = Fixture.CreateClient(opKey);

        var body = new
        {
            serviceAccountId = svc2Id,
            samples = new[]
            {
                new { schemaName = schema.Name, valueName = "count", value = 5, timestamp = DateTime.UtcNow, note = (string?)null },
            },
        };
        var response = await op.PostJsonAsync("/api/admin/submissions", body);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Rejecting_an_assigned_account_allowlist_entry_is_validated()
    {
        // A non-Service id cannot be assigned as a scope: the operator's allowlist must reference
        // real Service accounts. Supplying the admin's own id (a User/Admin) is rejected.
        var adminId = await AdminAccountIdAsync();
        var name = $"op-{Unique()}";
        var response = await Admin.PostJsonAsync("/api/admin/accounts", new
        {
            name,
            label = name,
            email = $"{name}@example.com",
            kind = "User",
            role = "Operator",
            enabled = true,
            assignedServiceIds = new[] { adminId },
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
