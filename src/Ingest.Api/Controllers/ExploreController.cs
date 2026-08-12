using Ingest.Api.Auth;
using Ingest.Api.Common;
using Ingest.Api.Models;
using Ingest.Core.Abstractions;
using Ingest.Core.Analytics;
using Ingest.Core.Common;
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
    /// <param name="anomaly">When <c>true</c>, score each bucket against its preceding history and populate the anomaly fields.</param>
    /// <param name="anomalyWindow">Rolling window (preceding buckets) the baseline uses; clamped server-side. Defaults to 12.</param>
    /// <param name="anomalyThreshold">The <c>|z|</c> cutoff at or above which a bucket is flagged; clamped server-side. Defaults to 2.5.</param>
    /// <param name="anomalyRobust">When <c>true</c>, use a median + MAD baseline instead of mean + standard deviation.</param>
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
        [FromQuery] bool anomaly,
        [FromQuery] int? anomalyWindow,
        [FromQuery] double? anomalyThreshold,
        [FromQuery] bool anomalyRobust,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(schema))
        {
            const string message = "The 'schema' query parameter is required.";
            return BadRequest(DiagnosticProblem.BadRequest(Diagnostic.Create(
                DiagnosticCodes.Api.MissingRequiredParameter,
                message,
                ("parameter", "schema"))));
        }

        // Confine the request to the caller's assigned services. A scoped caller asking only for
        // services outside its scope gets an empty (but well-formed) series rather than a leak.
        var effective = User.ResolveServiceFilter(serviceIds, out var empty);
        if (empty)
            return Ok(new ExploreSeriesResponse(schema, null, agg, from, to, new(), new()));

        var query = new ExploreSeriesQuery(
            schema, value, effective, from, to, agg,
            anomaly,
            anomalyWindow ?? AnomalyDetector.DefaultWindow,
            anomalyThreshold ?? AnomalyDetector.DefaultThreshold,
            anomalyRobust);
        var result = await explore.GetSeriesAsync(query, ct);
        return result is null
            ? NotFound(DiagnosticProblem.NotFound("Schema", schema))
            : Ok(ExploreSeriesResponse.FromResult(result));
    }

    /// <summary>
    /// Anomaly board for one period: each numeric value of the scanned schemas, with each applicable
    /// service's value for the period classified normal / anomaly / missing against its own recent
    /// history. Mirrors the scorecard's schema → value → per-service shape.
    /// </summary>
    /// <param name="schema">Restrict to these schemas (repeatable). Omit to scan every enabled schema.</param>
    /// <param name="serviceIds">Restrict to these services (repeatable). Omit for every service.</param>
    /// <param name="period">
    /// Which period to test: <see cref="ScorecardPeriod.Current"/> (default, the open period) or
    /// <see cref="ScorecardPeriod.LatestClosed"/> (the last elapsed period).
    /// </param>
    /// <param name="window">Rolling window (preceding periods) the baseline uses; clamped server-side. Defaults to 12.</param>
    /// <param name="threshold">The <c>|z|</c> cutoff at or above which a value is flagged; clamped server-side. Defaults to 2.5.</param>
    /// <param name="robust">When <c>true</c>, use a median + MAD baseline instead of mean + standard deviation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The anomaly board. <see cref="ExploreAnomalyResponse"/>.</response>
    [HttpGet("anomalies")]
    [ProducesResponseType(typeof(ExploreAnomalyResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAnomalies(
        [FromQuery(Name = "schema")] List<string>? schema,
        [FromQuery] List<Guid>? serviceIds,
        [FromQuery] ScorecardPeriod period,
        [FromQuery] int? window,
        [FromQuery] double? threshold,
        [FromQuery] bool robust,
        CancellationToken ct)
    {
        var effective = User.ResolveServiceFilter(serviceIds, out var empty);
        if (empty)
            return Ok(new ExploreAnomalyResponse(new(), new()));

        var query = new ExploreAnomalyQuery(
            schema, effective, period,
            window ?? AnomalyDetector.DefaultWindow,
            threshold ?? AnomalyDetector.DefaultThreshold,
            robust);
        var result = await explore.GetAnomaliesAsync(query, ct);
        return Ok(ExploreAnomalyResponse.FromResult(result));
    }

    /// <summary>
    /// Cross-schema RAG scorecard: every enabled schema's numeric values that carry a target band,
    /// with each reporting service's sample classified green/amber/red. Schemas and values with no
    /// banded history are omitted.
    /// </summary>
    /// <param name="serviceIds">Restrict to these services (repeatable). Omit for every service.</param>
    /// <param name="mode">
    /// <see cref="ScorecardMode.LatestAvailable"/> (default) shows each service's most recent sample;
    /// <see cref="ScorecardMode.LastPeriod"/> shows one period and marks non-reporting services as missing.
    /// </param>
    /// <param name="period">
    /// Which period <see cref="ScorecardMode.LastPeriod"/> reads: <see cref="ScorecardPeriod.Current"/>
    /// (default, the open period) or <see cref="ScorecardPeriod.LatestClosed"/> (the last elapsed period).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The scorecard. <see cref="ExploreScorecardResponse"/>.</response>
    [HttpGet("scorecard")]
    [ProducesResponseType(typeof(ExploreScorecardResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetScorecard(
        [FromQuery] List<Guid>? serviceIds,
        [FromQuery] ScorecardMode mode,
        [FromQuery] ScorecardPeriod period,
        CancellationToken ct)
    {
        var effective = User.ResolveServiceFilter(serviceIds, out var empty);
        if (empty)
            return Ok(new ExploreScorecardResponse(new(), new()));

        var result = await explore.GetScorecardAsync(new ExploreScorecardQuery(effective, mode, period), ct);
        return Ok(ExploreScorecardResponse.FromResult(result));
    }
}
