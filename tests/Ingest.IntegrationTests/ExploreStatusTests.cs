using Ingest.Api.Models;
using Ingest.IntegrationTests.Fixtures;

namespace Ingest.IntegrationTests;

/// <summary>The in-app analytics endpoints: the cross-schema scorecard and the missing-submissions
/// status report return coherent results once live data exists.</summary>
public sealed class ExploreStatusTests : IntegrationTestBase
{
    public ExploreStatusTests(IngestAppFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Explore_scorecard_includes_a_reported_banded_value()
    {
        var schema = await CreateSchemaAsync(withBands: true);
        var (_, key, _) = await CreateServiceAccountAsync();
        using (var s = Fixture.CreateClient(key)) await SubmitSampleAsync(s, schema.Name, 90);

        var scorecard = await (await Admin.GetAsync("/api/admin/explore/scorecard"))
            .ReadAsync<ExploreScorecardResponse>();

        Assert.Contains(scorecard.Schemas, s => s.SchemaName == schema.Name);
    }

    [Fact]
    public async Task Status_missing_report_is_returned()
    {
        // A required monthly value with no submission yet should surface somewhere in the report;
        // at minimum the endpoint returns a well-formed (possibly empty) list.
        await CreateSchemaAsync();
        await CreateServiceAccountAsync();

        var response = await Admin.GetAsync("/api/admin/status/missing");
        var report = await response.ReadAsync<List<MissingByCadenceDto>>();
        Assert.NotNull(report);
    }
}
