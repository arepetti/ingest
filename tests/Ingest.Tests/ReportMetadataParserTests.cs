using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Core.Reports;

namespace Ingest.Tests;

/// <summary>
/// Coverage for the YAML front-matter parser: it has to recognise the fence, parse the limited
/// set of supported keys, and pass the template body through unchanged.
/// </summary>
public class ReportMetadataParserTests
{
    [Fact]
    public void No_front_matter_returns_template_unchanged()
    {
        var content = "<p>Hello {{ name }}</p>";
        var meta = ReportMetadataParser.Parse(content);

        Assert.Null(meta.Name);
        Assert.Null(meta.Label);
        Assert.Null(meta.Type);
        Assert.Empty(meta.TargetSchemaNames);
        Assert.Equal(content, meta.Template);
    }

    [Fact]
    public void Parses_full_front_matter_block()
    {
        var content = """
            ---
            name: monthly_summary
            label: "Monthly summary"
            description: 'Aggregated KPIs over the period'
            type: Aggregate
            schemas: [waste_kpis, finance_kpis]
            ---
            <h1>{{ schema.label }}</h1>
            """;
        var meta = ReportMetadataParser.Parse(content);

        Assert.Equal("monthly_summary", meta.Name);
        Assert.Equal("Monthly summary", meta.Label);
        Assert.Equal("Aggregated KPIs over the period", meta.Description);
        Assert.Equal(ReportType.Aggregate, meta.Type);
        Assert.Equal(new[] { "waste_kpis", "finance_kpis" }, meta.TargetSchemaNames);
        Assert.StartsWith("<h1>{{ schema.label }}</h1>", meta.Template);
    }

    [Fact]
    public void Block_form_schemas_list_is_supported()
    {
        var content = """
            ---
            type: Single
            schemas:
              - alpha
              - beta
              - gamma
            ---
            body
            """;
        var meta = ReportMetadataParser.Parse(content);

        Assert.Equal(ReportType.Single, meta.Type);
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, meta.TargetSchemaNames);
        Assert.Equal("body", meta.Template.TrimEnd());
    }

    [Fact]
    public void Type_value_is_case_insensitive()
    {
        var content = "---\ntype: single\n---\nbody";
        Assert.Equal(ReportType.Single, ReportMetadataParser.Parse(content).Type);
    }

    [Fact]
    public void Unknown_keys_are_silently_ignored()
    {
        var content = """
            ---
            type: Aggregate
            future_field: whatever
            another: 42
            ---
            body
            """;
        var meta = ReportMetadataParser.Parse(content);
        Assert.Equal(ReportType.Aggregate, meta.Type);
        Assert.Equal("body", meta.Template.TrimEnd());
    }

    [Fact]
    public void Invalid_type_value_throws_validation_exception()
    {
        var content = "---\ntype: ContinuousLive\n---\nbody";
        var ex = Assert.Throws<ValidationException>(() => ReportMetadataParser.Parse(content));
        Assert.Contains(ex.Errors, e => e.Contains("type", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Unclosed_front_matter_throws_validation_exception()
    {
        var content = "---\ntype: Aggregate\nbody without closing fence";
        var ex = Assert.Throws<ValidationException>(() => ReportMetadataParser.Parse(content));
        Assert.Contains(ex.Errors, e => e.Contains("front matter", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Single_leading_newline_after_closing_fence_is_stripped()
    {
        var content = "---\ntype: Aggregate\n---\n<p>x</p>";
        var meta = ReportMetadataParser.Parse(content);
        Assert.Equal("<p>x</p>", meta.Template);
    }

    [Fact]
    public void Crlf_line_endings_are_supported()
    {
        var content = "---\r\ntype: Aggregate\r\nschemas: [a]\r\n---\r\nbody\r\n";
        var meta = ReportMetadataParser.Parse(content);
        Assert.Equal(ReportType.Aggregate, meta.Type);
        Assert.Equal(new[] { "a" }, meta.TargetSchemaNames);
        Assert.Equal("body\r\n", meta.Template);
    }
}
