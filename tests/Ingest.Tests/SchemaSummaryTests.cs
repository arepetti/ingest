using Ingest.Api.Odata;
using Ingest.Core.Entities;
using Microsoft.OData.Edm;

namespace Ingest.Tests;

/// <summary>
/// Tests for <see cref="SchemaSummary.From"/> and <see cref="SchemaValueSummary.From"/>: the
/// mappers behind the simplified <c>/odata/schemas</c> feed. The point of the feed is that it
/// carries only the labelling/bucketing/charting metadata — so the tests assert both that the
/// included fields round-trip and (via a whitelist) that nothing outside the agreed surface can
/// leak, even as the underlying <see cref="Schema"/>/<see cref="SchemaValue"/> grow new fields.
/// </summary>
public class SchemaSummaryTests
{
    private static Schema FullSchema() => new()
    {
        Name = "monthly_kpis",
        Label = "Monthly KPIs",
        Description = "Headline monthly figures.",
        Notes = "internal rationale — must NOT leak to the feed",
        Enabled = false,
        IsGlobal = false,
        ServiceIds = new List<Guid> { Guid.NewGuid() },
        Version = 7,
        SubmissionValidations = new List<string> { "tonnes >= 0" },
        Approval = new ApprovalPolicy { Mode = ApprovalMode.Required },
        Values = new List<SchemaValue>
        {
            new()
            {
                Name = "tonnes", Label = "Tonnes collected", Description = "Collected this month.",
                Type = SchemaValueType.Number, Unit = "t", Cadence = Cadence.Monthly,
                Required = true, Enabled = true,
                Min = 0, Max = 1000,
                AmberMin = 10, GreenMin = 50, GreenMax = 800, AmberMax = 900,
                // Excluded operational/constraint fields — should not surface on the summary.
                Notes = "secret", ValueValidation = "tonnes >= previous('tonnes')",
                RegexPattern = "x", MinLength = 1, MaxLength = 5,
            },
        },
    };

    [Fact]
    public void From_copies_the_included_schema_fields()
    {
        var s = SchemaSummary.From(FullSchema());

        Assert.Equal("monthly_kpis", s.Name);
        Assert.Equal("Monthly KPIs", s.Label);
        Assert.Equal("Headline monthly figures.", s.Description);
        Assert.False(s.Enabled);
        Assert.False(s.IsGlobal);
        var v = Assert.Single(s.Values);
        Assert.Equal("tonnes", v.Name);
    }

    [Fact]
    public void From_copies_the_included_value_fields_including_band_edges()
    {
        var v = Assert.Single(SchemaSummary.From(FullSchema()).Values);

        Assert.Equal("tonnes", v.Name);
        Assert.Equal("Tonnes collected", v.Label);
        Assert.Equal("Collected this month.", v.Description);
        Assert.Equal(SchemaValueType.Number, v.Type);
        Assert.Equal("t", v.Unit);
        Assert.Equal(Cadence.Monthly, v.Cadence);
        Assert.True(v.Required);
        Assert.True(v.Enabled);
        Assert.Equal(0, v.Min);
        Assert.Equal(1000, v.Max);
        Assert.Equal(10, v.AmberMin);
        Assert.Equal(50, v.GreenMin);
        Assert.Equal(800, v.GreenMax);
        Assert.Equal(900, v.AmberMax);
    }

    [Fact]
    public void Summary_types_expose_exactly_the_agreed_surface()
    {
        // Whitelist, not blacklist: a newly-added field on Schema/SchemaValue that gets accidentally
        // mapped (or a deliberate one that wasn't reviewed) trips this immediately.
        var schemaProps = typeof(SchemaSummary).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.Equal(
            new HashSet<string> { "Name", "Label", "Description", "Enabled", "IsGlobal", "Values" },
            schemaProps);

        var valueProps = typeof(SchemaValueSummary).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.Equal(
            new HashSet<string>
            {
                "Name", "Label", "Description", "Type", "Unit", "Cadence", "Required", "Enabled",
                "Min", "Max", "AmberMin", "GreenMin", "GreenMax", "AmberMax",
            },
            valueProps);
    }

    [Fact]
    public void From_maps_every_value_in_declaration_order()
    {
        var schema = new Schema
        {
            Name = "multi",
            Values = new List<SchemaValue>
            {
                new() { Name = "a", Type = SchemaValueType.Integer },
                new() { Name = "b", Type = SchemaValueType.String },
                new() { Name = "c", Type = SchemaValueType.Boolean },
            },
        };

        var names = SchemaSummary.From(schema).Values.Select(v => v.Name).ToArray();

        Assert.Equal(new[] { "a", "b", "c" }, names);
    }

    [Fact]
    public void From_handles_a_schema_with_no_values()
    {
        var s = SchemaSummary.From(new Schema { Name = "empty" });
        Assert.Empty(s.Values);
    }

    [Fact]
    public void From_leaves_band_edges_and_bounds_null_when_unset()
    {
        var schema = new Schema
        {
            Name = "plain",
            Values = new List<SchemaValue> { new() { Name = "x", Type = SchemaValueType.String } },
        };

        var v = Assert.Single(SchemaSummary.From(schema).Values);
        Assert.Null(v.Min);
        Assert.Null(v.Max);
        Assert.Null(v.AmberMin);
        Assert.Null(v.GreenMin);
        Assert.Null(v.GreenMax);
        Assert.Null(v.AmberMax);
        Assert.Null(v.Label);
    }

    [Fact]
    public void Edm_model_exposes_pascal_case_columns_and_a_keyed_schemas_set()
    {
        // Guards against re-introducing EnableLowerCamelCase (which would rename every wire column
        // to camelCase and silently break the documented feeds + Power BI examples).
        var model = EdmModelBuilderExtensions.BuildEdmModel();

        var samples = Assert.IsAssignableFrom<IEdmEntityType>(model.FindDeclaredType($"{typeof(SampleProjection).Namespace}.{nameof(SampleProjection)}"));
        Assert.NotNull(samples.FindProperty("SchemaName"));
        Assert.Null(samples.FindProperty("schemaName"));

        // SchemaSummary is an entity keyed on Name; SchemaValueSummary is a nested complex type.
        var summary = Assert.IsAssignableFrom<IEdmEntityType>(model.FindDeclaredType($"{typeof(SchemaSummary).Namespace}.{nameof(SchemaSummary)}"));
        Assert.Equal("Name", Assert.Single(summary.Key()).Name);
        Assert.NotNull(summary.FindProperty("Values"));
        Assert.IsAssignableFrom<IEdmComplexType>(model.FindDeclaredType($"{typeof(SchemaValueSummary).Namespace}.{nameof(SchemaValueSummary)}"));
    }
}
