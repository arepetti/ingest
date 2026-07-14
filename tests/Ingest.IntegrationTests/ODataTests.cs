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

    [Fact]
    public async Task Events_feed_exposes_kind_and_effective_end()
    {
        var label = $"evt-{Unique()}";
        var start = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);
        await Admin.PostJsonAsync("/api/admin/events", new
        {
            timestamp = start,
            label,
            kind = "Interval",
            durationMinutes = 120,
            serviceIds = Array.Empty<Guid>(),
        });

        var values = await ODataValuesAsync($"/odata/events?$filter=Label eq '{label}'");
        Assert.Equal(1, values.GetArrayLength());
        var row = values[0];
        Assert.Equal("Interval", row.GetProperty("Kind").GetString());
        Assert.Equal(120, row.GetProperty("DurationMinutes").GetInt32());
        // Compare as DateTimeOffset (instant-equal) rather than DateTime: OData may render the UTC
        // instant with a non-"Z" numeric offset, which is an equivalent instant, not a different one.
        Assert.Equal(new DateTimeOffset(start.AddHours(2), TimeSpan.Zero), row.GetProperty("EffectiveEnd").GetDateTimeOffset());
    }

    [Fact]
    public async Task Events_feed_lets_a_client_query_an_open_ended_event_by_interval()
    {
        // A FromNowOn event has a null EffectiveEnd, so "does this overlap window X" is expressed as
        // Timestamp le <windowEnd> and (EffectiveEnd eq null or EffectiveEnd ge <windowStart>).
        var label = $"evt-{Unique()}";
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await Admin.PostJsonAsync("/api/admin/events", new
        {
            timestamp = start,
            label,
            kind = "FromNowOn",
            serviceIds = Array.Empty<Guid>(),
        });

        var filter = $"$filter=Label eq '{label}' and Timestamp le 2026-06-01T00:00:00Z and (EffectiveEnd eq null or EffectiveEnd ge 2026-05-01T00:00:00Z)";
        var values = await ODataValuesAsync($"/odata/events?{filter}");
        Assert.Equal(1, values.GetArrayLength());
        Assert.True(values[0].GetProperty("EffectiveEnd").ValueKind == System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Events_feed_supports_filtering_by_service_scope()
    {
        var (serviceId, _, _) = await CreateServiceAccountAsync();
        var scopedLabel = $"evt-{Unique()}";
        var globalLabel = $"evt-{Unique()}";
        await Admin.PostJsonAsync("/api/admin/events", new
        {
            timestamp = DateTime.UtcNow,
            label = scopedLabel,
            kind = "PointInTime",
            serviceIds = new[] { serviceId },
        });
        await Admin.PostJsonAsync("/api/admin/events", new
        {
            timestamp = DateTime.UtcNow,
            label = globalLabel,
            kind = "PointInTime",
            serviceIds = Array.Empty<Guid>(),
        });

        var scoped = await ODataValuesAsync($"/odata/events?$filter=ServiceIds/any(s: s eq {serviceId})");
        Assert.Contains(scoped.EnumerateArray(), e => e.GetProperty("Label").GetString() == scopedLabel);
        Assert.DoesNotContain(scoped.EnumerateArray(), e => e.GetProperty("Label").GetString() == globalLabel);

        var global = await ODataValuesAsync($"/odata/events?$filter=ServiceIds/$count eq 0 and Label eq '{globalLabel}'");
        Assert.Equal(1, global.GetArrayLength());
    }
}
