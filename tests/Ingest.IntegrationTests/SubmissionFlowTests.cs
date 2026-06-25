using Ingest.Api.Models;
using Ingest.IntegrationTests.Fixtures;

namespace Ingest.IntegrationTests;

/// <summary>The core happy path: an admin defines a schema, a service submits against it, and the
/// sample becomes visible through both the service API and the OData feed.</summary>
public sealed class SubmissionFlowTests : IntegrationTestBase
{
    public SubmissionFlowTests(IngestAppFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Service_submits_and_sample_is_live_and_queryable()
    {
        var schema = await CreateSchemaAsync();
        var (_, apiKey, _) = await CreateServiceAccountAsync();
        using var service = Fixture.CreateClient(apiKey);

        // A global schema is visible to every service.
        var visible = await (await service.GetAsync("/api/schemas")).ReadJsonAsync();
        Assert.Contains(visible.EnumerateArray(), s => s.GetProperty("name").GetString() == schema.Name);

        var submissionId = await SubmitSampleAsync(service, schema.Name, value: 42);

        // The service can read its own submission back.
        var mine = await (await service.GetAsync($"/api/submissions/{submissionId}")).ReadAsync<SubmissionDto>();
        Assert.Equal(submissionId, mine.Id);
        Assert.Equal("NotRequired", mine.ApprovalStatus.ToString());
        Assert.Contains(mine.Samples, s => s.ValueName == "count");

        // And it shows up live in the OData projection.
        var values = await ODataValuesAsync($"/odata/samples?$filter=SubmissionId eq {submissionId}");
        Assert.Equal(1, values.GetArrayLength());
        Assert.Equal(42, values[0].GetProperty("IntegerValue").GetInt64());
        Assert.Equal(schema.Name, values[0].GetProperty("SchemaName").GetString());
    }
}
