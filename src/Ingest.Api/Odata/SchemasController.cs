using Ingest.Api.Auth;
using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;

namespace Ingest.Api.Odata;

/// <summary>
/// OData feed over a simplified, read-only schema catalogue (<see cref="SchemaSummary"/>). It lets
/// a BI tool pull schema/value labels, units, types, cadences and the charting band edges as a
/// second query and join them to the <c>/odata/samples</c> rows on name — replacing the manual
/// <c>/api/admin/schemas</c> JSON join. Schemas are few (a handful), so the whole live catalogue is
/// materialised in memory and returned as an <see cref="IQueryable{T}"/> for OData to filter; the
/// natural lever is <c>$filter=name eq '…'</c> (or <c>name in (…)</c>). Requires the
/// <c>schemas:read</c> capability.
/// </summary>
[Authorize(Policy = Capabilities.SchemasRead)]
public sealed class SchemasController : ODataController
{
    private readonly ISchemaRepository _schemas;

    /// <summary>Create a new <see cref="SchemasController"/>.</summary>
    /// <param name="schemas">Repository over the schema catalogue.</param>
    public SchemasController(ISchemaRepository schemas)
    {
        _schemas = schemas;
    }

    // A schema deployment is tiny (typically well under a hundred), so a single max-size page
    // covers the whole live catalogue; we loop defensively in case it ever grows.
    private const int PageSize = 500;

    /// <summary>Return the live schema catalogue as simplified summaries. OData applies the query.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The non-deleted schemas mapped to <see cref="SchemaSummary"/>.</returns>
    [HttpGet("odata/schemas")]
    [EnableQuery(PageSize = PageSize, MaxTop = 5000)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var summaries = new List<SchemaSummary>();
        for (var pageNo = 1; ; pageNo++)
        {
            var page = await _schemas.ListAsync(new PageRequest(pageNo, PageSize), ct);
            summaries.AddRange(page.Items.Select(SchemaSummary.From));
            if (page.Items.Count < PageSize || summaries.Count >= page.Total) break;
        }

        return Ok(summaries.AsQueryable());
    }
}
