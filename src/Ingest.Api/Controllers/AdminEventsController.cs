using Ingest.Api.Common;
using Ingest.Api.Models;
using Ingest.Core.Abstractions;
using Ingest.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ingest.Api.Controllers;

/// <summary>
/// Admin management of the events timeline — point-in-time occurrences (e.g. a maintenance window,
/// an incident, a deployment) optionally scoped to the services they affect.
/// </summary>
[ApiController]
[Route("api/admin/events")]
[Authorize(Policy = Capabilities.EventsRead)]
public sealed class AdminEventsController(IEventsService events) : ControllerBase
{
    /// <summary>List events in a paged form, newest first.</summary>
    /// <param name="page">1-based page number; defaults to 1 when omitted.</param>
    /// <param name="pageSize">Page size; defaults to 50.</param>
    /// <param name="from">Inclusive lower bound on the event's span (start for Interval/FromNowOn); omit for no lower bound.</param>
    /// <param name="to">Exclusive upper bound on the event's span; omit for no upper bound.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">A page of events.</response>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<EventDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var result = await events.ListAsync(RequestHelpers.ToPageRequest(page, pageSize, null, false), from, to, ct);
        return Ok(result.Map(EventDto.From));
    }

    /// <summary>Create a new event.</summary>
    /// <param name="body">The event to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="201">The created event.</response>
    /// <response code="400">The event is invalid (missing label/timestamp, or an unknown service id).</response>
    [HttpPost]
    [Authorize(Policy = Capabilities.EventsManage)]
    [ProducesResponseType(typeof(EventDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] UpsertEventRequest body, CancellationToken ct)
    {
        var created = await events.CreateAsync(body.ToEntity(), ct);
        return Created($"/api/admin/events/{created.Id}", EventDto.From(created));
    }

    /// <summary>Replace an existing event.</summary>
    /// <param name="id">Id of the event to update.</param>
    /// <param name="body">The new event contents.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The updated event.</response>
    /// <response code="400">The event is invalid.</response>
    /// <response code="404">No event with that id.</response>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Capabilities.EventsManage)]
    [ProducesResponseType(typeof(EventDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpsertEventRequest body, CancellationToken ct)
    {
        var updated = await events.UpdateAsync(id, body.ToEntity(), ct);
        return Ok(EventDto.From(updated));
    }

    /// <summary>Delete an event.</summary>
    /// <param name="id">Id of the event to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="204">The event was deleted.</response>
    /// <response code="404">No event with that id.</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Capabilities.EventsManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await events.DeleteAsync(id, ct);
        return NoContent();
    }
}
