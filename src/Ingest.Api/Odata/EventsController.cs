using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;

namespace Ingest.Api.Odata;

/// <summary>
/// OData feed over the admin-recorded events timeline (<see cref="EventFeedItem"/>), for BI tools
/// that want to overlay maintenance windows / incidents / deployments on their own charts. Events
/// are a small, admin-curated annotation set, so the whole live (non-deleted) list is materialised
/// in memory and returned as an <see cref="IQueryable{T}"/> for OData to filter — the same approach
/// as <see cref="SchemasController"/>. Requires the <c>events:read</c> capability.
/// </summary>
[Authorize(Policy = Capabilities.EventsRead)]
public sealed class EventsController : ODataController
{
    private readonly IEventsService _events;

    /// <summary>Create a new <see cref="EventsController"/>.</summary>
    /// <param name="events">Service over the events timeline.</param>
    public EventsController(IEventsService events)
    {
        _events = events;
    }

    // Events are admin-curated annotations, not bulk telemetry — a handful to a few hundred over a
    // deployment's life. One or two pages comfortably covers that; loop defensively in case it grows.
    private const int PageSize = 500;

    /// <summary>Return every live event as a feed item. OData applies the query.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The non-deleted events mapped to <see cref="EventFeedItem"/>.</returns>
    [HttpGet("odata/events")]
    [EnableQuery(PageSize = PageSize, MaxTop = 5000)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var items = new List<EventFeedItem>();
        for (var pageNo = 1; ; pageNo++)
        {
            var page = await _events.ListAsync(new PageRequest(pageNo, PageSize), ct: ct);
            items.AddRange(page.Items.Select(EventFeedItem.From));
            if (page.Items.Count < PageSize || items.Count >= page.Total) break;
        }

        return Ok(items.AsQueryable());
    }
}
