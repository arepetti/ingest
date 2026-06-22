using Ingest.Api.Auth;
using Ingest.Core.Abstractions;
using Ingest.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;

namespace Ingest.Api.Odata;

/// <summary>
/// OData feed over the computed cross-schema RAG scorecard, exposed as the unbound function
/// <c>scorecard(mode,period)</c>. Returns one flat <see cref="ScorecardCard"/> per
/// (schema, value, service) cell — including the target band edges and the RAG status as text —
/// so PowerBI can pivot it directly. <c>$filter</c>, <c>$select</c>, <c>$orderby</c>,
/// <c>$top</c>/<c>$skip</c> and <c>$count</c> are supported on top of the result. Requires the
/// <c>query:read</c> capability, like the rest of the OData surface.
/// </summary>
[Authorize(Policy = Capabilities.QueryRead)]
public sealed class ScorecardController : ODataController
{
    private readonly IExploreService _explore;

    /// <summary>Create a new <see cref="ScorecardController"/>.</summary>
    /// <param name="explore">Explore service that computes the scorecard.</param>
    public ScorecardController(IExploreService explore)
    {
        _explore = explore;
    }

    /// <summary>
    /// Invoke the scorecard function. <paramref name="mode"/> accepts <c>LatestAvailable</c>
    /// (default) or <c>LastPeriod</c>; <paramref name="period"/> accepts <c>Current</c> (default)
    /// or <c>LatestClosed</c> and only matters in last-period mode. Unknown values fall back to the
    /// defaults. Service scoping is left to the BI client via <c>$filter</c>.
    /// </summary>
    /// <param name="mode">Which sample represents each service.</param>
    /// <param name="period">Which period last-period mode reads.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The flat scorecard cards.</returns>
    [HttpGet("odata/scorecard(mode={mode},period={period})")]
    [EnableQuery(PageSize = 500, MaxTop = 5000)]
    public async Task<IActionResult> Scorecard(
        [FromODataUri] string? mode,
        [FromODataUri] string? period,
        CancellationToken ct)
    {
        var scMode = Enum.TryParse<ScorecardMode>(mode, ignoreCase: true, out var m)
            ? m
            : ScorecardMode.LatestAvailable;
        var scPeriod = Enum.TryParse<ScorecardPeriod>(period, ignoreCase: true, out var p)
            ? p
            : ScorecardPeriod.Current;

        var result = await _explore.GetScorecardAsync(
            new ExploreScorecardQuery(ServiceIds: null, scMode, scPeriod), ct);

        return Ok(ScorecardCard.FromResult(result).AsQueryable());
    }
}
