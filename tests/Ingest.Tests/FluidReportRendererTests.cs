using Ingest.Core.Common;
using Ingest.Infrastructure.Reports;

namespace Ingest.Tests;

/// <summary>
/// Sanity-check the Liquid renderer: simple substitution, iteration over a list, and the
/// "syntax error becomes a 400" behaviour the API relies on.
/// </summary>
public class FluidReportRendererTests
{
    [Fact]
    public async Task Renders_top_level_variables()
    {
        var renderer = new FluidReportRenderer();
        var html = await renderer.RenderAsync("Hello {{ name }}!", new { name = "World" });
        Assert.Equal("Hello World!", html);
    }

    [Fact]
    public async Task Iterates_over_nested_anonymous_objects()
    {
        var renderer = new FluidReportRenderer();
        var model = new
        {
            schema = new { label = "KPIs" },
            values = new[]
            {
                new { label = "Tonnes", buckets = new[] { new { count = 3 }, new { count = 7 } } },
                new { label = "Incidents", buckets = new[] { new { count = 1 } } },
            },
        };

        var template = """
            <h1>{{ schema.label }}</h1>
            {% for v in values %}<div>{{ v.label }}: {% for b in v.buckets %}{{ b.count }} {% endfor %}</div>
            {% endfor %}
            """;
        var html = await renderer.RenderAsync(template, model);
        Assert.Contains("KPIs", html);
        Assert.Contains("Tonnes: 3 7", html);
        Assert.Contains("Incidents: 1", html);
    }

    [Fact]
    public async Task Invalid_syntax_throws_validation_exception()
    {
        var renderer = new FluidReportRenderer();
        // Unterminated tag — should be rejected at parse time.
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => renderer.RenderAsync("{% if a %}", new { a = true }));
        Assert.Contains(ex.Errors, e => e.Contains("template", StringComparison.OrdinalIgnoreCase));
    }
}
