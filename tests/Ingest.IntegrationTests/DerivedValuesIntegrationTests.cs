using System.Net;
using Ingest.IntegrationTests.Fixtures;

namespace Ingest.IntegrationTests;

/// <summary>
/// End-to-end coverage for calculated schema values: derived rows in OData and rejection when submitted.
/// </summary>
public class DerivedValuesIntegrationTests : IntegrationTestBase
{
    public DerivedValuesIntegrationTests(IngestAppFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Submit_base_values_materialises_derived_row_in_OData()
    {
        var (serviceId, apiKey, _) = await CreateServiceAccountAsync();
        using var svc = Fixture.CreateClient(apiKey);

        var schemaName = $"derived_{Unique()}";
        await Admin.PostJsonAsync("/api/admin/schemas", new
        {
            name = schemaName,
            label = schemaName,
            modifiable = true,
            enabled = true,
            isGlobal = true,
            version = 1,
            values = new object[]
            {
                new { name = "a", label = "A", type = "Number", cadence = "Monthly", required = true, modifiable = true, enabled = true },
                new { name = "total", label = "Total", type = "Number", cadence = "Monthly", required = false, modifiable = true, enabled = true, kind = "Calculated", expression = "a * 2" },
            },
        });

        await SubmitNumberAsync(svc, schemaName, "a", 7.5);

        var values = await ODataValuesAsync($"/odata/samples?$filter=SchemaName eq '{schemaName}'");
        Assert.Equal(2, values.GetArrayLength());

        var derived = values.EnumerateArray().Single(e => e.GetProperty("ValueName").GetString() == "total");
        Assert.True(derived.GetProperty("IsDerived").GetBoolean());
        Assert.Equal(15d, derived.GetProperty("NumberValue").GetDouble());
    }

    [Fact]
    public async Task Schema_validation_rule_can_reference_calculated_value()
    {
        var (_, apiKey, _) = await CreateServiceAccountAsync();
        using var svc = Fixture.CreateClient(apiKey);

        var schemaName = $"derived_{Unique()}";
        await Admin.PostJsonAsync("/api/admin/schemas", new
        {
            name = schemaName,
            label = schemaName,
            modifiable = true,
            enabled = true,
            isGlobal = true,
            version = 1,
            submissionValidations = new[] { "if(total < 0, 'total must be non-negative', null)" },
            values = new object[]
            {
                new { name = "a", label = "A", type = "Number", cadence = "Monthly", required = true, modifiable = true, enabled = true },
                new { name = "total", label = "Total", type = "Number", cadence = "Monthly", kind = "Calculated", expression = "a * 2" },
            },
        });

        var ok = await svc.PostJsonAsync("/api/submissions/validate", new
        {
            samples = new[]
            {
                new { schemaName, valueName = "a", value = 5, timestamp = DateTime.UtcNow, note = (string?)null },
            },
        });
        ok.EnsureSuccessStatusCode();
        var verdict = await ok.ReadJsonAsync();
        Assert.True(verdict.GetProperty("valid").GetBoolean());

        var bad = await svc.PostJsonAsync("/api/submissions/validate", new
        {
            samples = new[]
            {
                new { schemaName, valueName = "a", value = -3, timestamp = DateTime.UtcNow, note = (string?)null },
            },
        });
        bad.EnsureSuccessStatusCode();
        var badVerdict = await bad.ReadJsonAsync();
        Assert.False(badVerdict.GetProperty("valid").GetBoolean());
    }

    [Fact]
    public async Task Submitting_calculated_value_is_rejected()
    {
        var (_, apiKey, _) = await CreateServiceAccountAsync();
        using var svc = Fixture.CreateClient(apiKey);

        var schemaName = $"derived_{Unique()}";
        await Admin.PostJsonAsync("/api/admin/schemas", new
        {
            name = schemaName,
            label = schemaName,
            modifiable = true,
            enabled = true,
            isGlobal = true,
            version = 1,
            values = new object[]
            {
                new { name = "a", label = "A", type = "Number", cadence = "Monthly", required = false, modifiable = true, enabled = true },
                new { name = "total", label = "Total", type = "Number", cadence = "Monthly", kind = "Calculated", expression = "a + 1" },
            },
        });

        var resp = await svc.PostJsonAsync("/api/submissions", new
        {
            samples = new[]
            {
                new { schemaName, valueName = "total", value = 99, timestamp = DateTime.UtcNow, note = (string?)null },
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var problem = await resp.ReadJsonBodyAsync();
        var detail = problem.GetProperty("detail").GetString() ?? "";
        Assert.Contains("calculated", detail, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task SubmitNumberAsync(HttpClient serviceClient, string schemaName, string valueName, double value)
    {
        var body = new
        {
            samples = new[]
            {
                new { schemaName, valueName, value, timestamp = DateTime.UtcNow, note = (string?)null },
            },
        };
        (await serviceClient.PostJsonAsync("/api/submissions", body)).EnsureSuccessStatusCode();
    }
}
