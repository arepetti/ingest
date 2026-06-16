using Ingest.Api.Auth;
using Ingest.Api.Models;
using Ingest.Core.Abstractions;
using Ingest.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ingest.Api.Controllers;

/// <summary>
/// Lightweight in-app analytics for the bundled "Explore" page. Aggregates the denormalised sample
/// projection into per-cadence buckets with a per-service breakdown so operators can chart trends,
/// compare services and read off the latest period without an external BI tool. Operator/Admin.
/// </summary>
/// <remarks>
/// This is a convenience for deployments without Power BI/Excel, not a replacement for them: the
/// query is capped and intentionally narrow (one schema, numeric values, server-side aggregation).
/// For ad-hoc analysis at scale use the OData feed or <c>POST /api/admin/query</c>.
/// </remarks>
[ApiController]
[Route("api/admin/explore")]
[Authorize(Policy = Capabilities.ExploreRead)]
public sealed class ExploreController(IExploreService explore) : ControllerBase
{
    /// <summary>
    /// Build a per-value, per-cadence, per-service series for one schema. Only numeric values are
    /// returned; non-numeric and unknown value names are ignored.
    /// </summary>
    /// <param name="schema">Machine-style schema name to explore. Required.</param>
    /// <param name="value">Restrict to these value names (repeatable). Omit for every numeric value.</param>
    /// <param name="serviceIds">Restrict to these services (repeatable). Omit for every service.</param>
    /// <param name="from">Inclusive lower bound on the sample timestamp.</param>
    /// <param name="to">Exclusive upper bound on the sample timestamp.</param>
    /// <param name="agg">How each bucket reduces its samples; defaults to <see cref="ExploreAggregation.Average"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The series. <see cref="ExploreSeriesResponse"/>.</response>
    /// <response code="404">No schema with that name.</response>
    [HttpGet("series")]
    [ProducesResponseType(typeof(ExploreSeriesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSeries(
        [FromQuery] string schema,
        [FromQuery(Name = "value")] List<string>? value,
        [FromQuery] List<Guid>? serviceIds,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] ExploreAggregation agg,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(schema))
            return BadRequest(new { error = "The 'schema' query parameter is required." });

        var query = new ExploreSeriesQuery(schema, value, serviceIds, from, to, agg);
        var result = await explore.GetSeriesAsync(query, ct);
        return result is null ? NotFound() : Ok(ExploreSeriesResponse.FromResult(result));
    }
}
