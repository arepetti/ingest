using Ingest.Core.Common;
using Ingest.Core.Entities;

namespace Ingest.Core.Abstractions;

/// <summary>
/// CRUD access to the admin-recorded events timeline. Events are soft-deleted so audit history is
/// preserved, and are optionally scoped to a set of services via <see cref="Event.ServiceIds"/>
/// (empty means "all services").
/// </summary>
public interface IEventsService
{
    /// <summary>
    /// Page through events (excluding soft-deleted ones), newest first. When <paramref name="from"/>
    /// and/or <paramref name="to"/> are supplied, an event is included when its span — a single
    /// instant for <see cref="EventKind.PointInTime"/>, <c>[Timestamp, Timestamp+Duration]</c> for
    /// <see cref="EventKind.Interval"/>, or the open-ended <c>[Timestamp, +∞)</c> for
    /// <see cref="EventKind.FromNowOn"/> — overlaps the half-open window <c>[from, to)</c>.
    /// </summary>
    /// <param name="request">Paging shape.</param>
    /// <param name="from">Inclusive lower bound on the event's span, or <c>null</c> for no lower bound.</param>
    /// <param name="to">Exclusive upper bound on the event's span, or <c>null</c> for no upper bound.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<PagedResult<Event>> ListAsync(PageRequest request, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);

    /// <summary>Create a new event. <see cref="Event.Label"/> and <see cref="Event.Timestamp"/> are required; <see cref="Event.ServiceIds"/> must all reference existing service accounts.</summary>
    Task<Event> CreateAsync(Event ev, CancellationToken ct = default);

    /// <summary>Replace an existing event by id. Throws <see cref="NotFoundException"/> when it doesn't exist.</summary>
    Task<Event> UpdateAsync(Guid id, Event ev, CancellationToken ct = default);

    /// <summary>Soft-delete an event by id.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
