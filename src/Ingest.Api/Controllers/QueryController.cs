using Ingest.Api.Auth;
using Ingest.Api.Common;
using Ingest.Api.Models;
using Ingest.Core.Abstractions;
using Ingest.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ingest.Api.Controllers;

/// <summary>
/// Operator-facing ad-hoc sample query over the denormalised <c>SampleProjection</c> store. Use
/// this when a reporting tool (PowerBI, a custom dashboard, …) needs JSON-shaped paged results
/// rather than the OData feed served at <c>/odata</c>.
/// </summary>
[ApiController]
[Route("api/admin/query")]
[Authorize(Policy = Capabilities.QueryRead)]
public sealed class QueryController(ISampleRepository samples) : ControllerBase
{
    /// <summary>Query the flat sample projection.</summary>
    /// <remarks>
    /// All filters are optional and AND-combined. Setting <c>latestOnly</c> trims the result to
    /// the most recent sample per (service, schema, value) tuple — useful for dashboards that
    /// only need a snapshot. Deleted samples are excluded unless explicitly requested.
    /// </remarks>
    /// <param name="req">Filters and paging.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">A page of sample projections.</response>
    [HttpPost]
    [ProducesResponseType(typeof(PagedResponse<SampleProjectionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> QuerySamples([FromBody] QueryRequest req, CancellationToken ct)
    {
        // Confine the query to the caller's assigned services (intersected with any explicit
        // serviceIds filter). A scoped caller asking only for out-of-scope services gets an empty
        // page rather than a leak.
        var effective = User.ResolveServiceFilter(req.ServiceIds, out var empty);
        if (empty)
            return Ok(new PagedResponse<SampleProjectionDto>(Array.Empty<SampleProjectionDto>(), 0, req.Page, req.PageSize));

        var q = new SampleQuery(
            effective,
            req.SchemaNames,
            req.From,
            req.To,
            req.LatestOnly,
            req.IncludeDeleted,
            req.Page,
            req.PageSize,
            req.Sort);
        var result = await samples.QueryAsync(q, ct);
        return Ok(result.Map(SampleProjectionDto.From));
    }
}
