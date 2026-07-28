using System.Net;
using Ingest.Api.Controllers;
using Ingest.Api.Models;
using Ingest.IntegrationTests.Fixtures;

namespace Ingest.IntegrationTests;

/// <summary>
/// <c>POST /api/expressions/dependencies</c> — the batch identifier-resolution endpoint behind the
/// schema editor's "Dependencies" diagram. It parses every expression with the real NCalc engine
/// (same as <c>/translate</c>/<c>/validate</c>) so the diagram is a real dependency walk rather than
/// a client-side guess.
/// </summary>
public sealed class ExpressionDependenciesTests : IntegrationTestBase
{
    public ExpressionDependenciesTests(IngestAppFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Resolves_identifiers_for_each_expression_in_order()
    {
        var response = await Admin.PostJsonAsync("/api/expressions/dependencies", new
        {
            expressions = new[] { "peak / average", "vehicle_breakdowns >= 2", "'a literal string' != other" },
        });
        var body = await response.ReadAsync<ExpressionDependencyBatchResponse>();

        Assert.Equal(3, body.Results.Count);
        Assert.Equal(new[] { "average", "peak" }, body.Results[0].Identifiers.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Contains("vehicle_breakdowns", body.Results[1].Identifiers);
        Assert.Contains("other", body.Results[2].Identifiers);
        // The quoted literal must not leak through as a referenced identifier.
        Assert.DoesNotContain(body.Results[2].Identifiers, id => id.Contains("literal", StringComparison.OrdinalIgnoreCase));
        Assert.All(body.Results, r => Assert.Null(r.Error));
    }

    [Fact]
    public async Task Bound_key_references_keep_their_minimum_maximum_suffix()
    {
        var response = await Admin.PostJsonAsync("/api/expressions/dependencies", new
        {
            expressions = new[] { "[weight.minimum] < weight" },
        });
        var body = await response.ReadAsync<ExpressionDependencyBatchResponse>();

        Assert.Contains("weight.minimum", body.Results[0].Identifiers, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("weight", body.Results[0].Identifiers, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_malformed_expression_fails_only_its_own_entry()
    {
        var response = await Admin.PostJsonAsync("/api/expressions/dependencies", new
        {
            expressions = new[] { "peak > 0", "not a real expression(", "average > 0" },
        });
        var body = await response.ReadAsync<ExpressionDependencyBatchResponse>();

        Assert.Equal(3, body.Results.Count);
        Assert.Null(body.Results[0].Error);
        Assert.Contains("peak", body.Results[0].Identifiers);
        Assert.NotNull(body.Results[1].Error);
        Assert.Empty(body.Results[1].Identifiers);
        Assert.Null(body.Results[2].Error);
        Assert.Contains("average", body.Results[2].Identifiers);
    }

    [Fact]
    public async Task Blank_entries_resolve_to_no_identifiers_and_no_error()
    {
        var response = await Admin.PostJsonAsync("/api/expressions/dependencies", new
        {
            expressions = new[] { "", "   " },
        });
        var body = await response.ReadAsync<ExpressionDependencyBatchResponse>();

        Assert.Equal(2, body.Results.Count);
        Assert.All(body.Results, r => { Assert.Empty(r.Identifiers); Assert.Null(r.Error); });
    }

    [Fact]
    public async Task Empty_batch_returns_an_empty_result_list()
    {
        var response = await Admin.PostJsonAsync("/api/expressions/dependencies", new { expressions = Array.Empty<string>() });
        var body = await response.ReadAsync<ExpressionDependencyBatchResponse>();
        Assert.Empty(body.Results);
    }

    [Fact]
    public async Task Oversized_batch_is_rejected_with_400()
    {
        var response = await Admin.PostJsonAsync("/api/expressions/dependencies", new
        {
            expressions = Enumerable.Repeat("a", ExpressionsController.MaxDependencyBatchSize + 1).ToArray(),
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Missing_expressions_array_is_rejected_with_400()
    {
        var response = await Admin.PostJsonAsync("/api/expressions/dependencies", new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Endpoint_does_not_require_authentication()
    {
        using var anonymous = Fixture.CreateClient(null);
        var response = await anonymous.PostJsonAsync("/api/expressions/dependencies", new
        {
            expressions = new[] { "a + b" },
        });
        response.EnsureSuccessStatusCode();
        var body = await response.ReadAsync<ExpressionDependencyBatchResponse>();
        Assert.Equal(new[] { "a", "b" }, body.Results[0].Identifiers.OrderBy(x => x, StringComparer.Ordinal));
    }
}
