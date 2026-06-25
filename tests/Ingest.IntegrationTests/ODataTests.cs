using Ingest.IntegrationTests.Fixtures;

namespace Ingest.IntegrationTests;

/// <summary>Exercises the OData surface PowerBI/Excel consume: the samples feed (with the common
/// query options), the schema catalogue, and the computed scorecard function.</summary>
public sealed class ODataTests : IntegrationTestBase
{
    public ODataTests(IngestAppFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Samples_feed_supports_filter_top_count_and_orderby()
    {
        var schema = await CreateSchemaAsync(withBands: true);

        // Two services each report once -> two live rows for this schema.
        var (_, key1, _) = await CreateServiceAccountAsync();
        var (_, key2, _) = await CreateServiceAccountAsync();
        using (var s1 = Fixture.CreateClient(key1)) await SubmitSampleAsync(s1, schema.Name, 90);
        using (var s2 = Fixture.CreateClient(key2)) await SubmitSampleAsync(s2, schema.Name, 60);

        var baseFilter = $"$filter=SchemaName eq '{schema.Name}'";

        // $filter
        var all = await ODataValuesAsync($"/odata/samples?{baseFilter}");
        Assert.Equal(2, all.GetArrayLength());

        // $top
        var top1 = await ODataValuesAsync($"/odata/samples?{baseFilter}&$top=1");
        Assert.Equal(1, top1.GetArrayLength());

        // $count
        var counted = await (await Admin.GetAsync($"/odata/samples?{baseFilter}&$count=true")).ReadJsonAsync();
        Assert.Equal(2, counted.GetProperty("@odata.count").GetInt32());

        // $orderby (descending) puts the largest reported value first.
        var ordered = await ODataValuesAsync($"/odata/samples?{baseFilter}&$orderby=IntegerValue desc&$top=1");
        Assert.Equal(90, ordered[0].GetProperty("IntegerValue").GetInt64());
    }

    [Fact]
    public async Task Schemas_feed_exposes_the_catalogue()
    {
        var schema = await CreateSchemaAsync();
        var values = await ODataValuesAsync($"/odata/schemas?$filter=Name eq '{schema.Name}'");
        Assert.Equal(1, values.GetArrayLength());
        Assert.Equal(schema.Name, values[0].GetProperty("Name").GetString());
    }

    [Fact]
    public async Task Scorecard_function_returns_cards_for_banded_values()
    {
        var schema = await CreateSchemaAsync(withBands: true);
        var (_, key, _) = await CreateServiceAccountAsync();
        using (var s = Fixture.CreateClient(key)) await SubmitSampleAsync(s, schema.Name, 90);

        var cards = await ODataValuesAsync("/odata/scorecard(mode='LatestAvailable',period='Current')");
        Assert.Contains(cards.EnumerateArray(), c => c.GetProperty("SchemaName").GetString() == schema.Name);
    }
}
