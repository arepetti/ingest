using Ingest.Core.Entities;

namespace Ingest.Api.Odata;

/// <summary>
/// Read-only projection of an admin-recorded <see cref="Event"/> for the <c>/odata/events</c> feed.
/// Adds <see cref="EffectiveEnd"/> — the computed end of the event's span, or <c>null</c> for an
/// open-ended <see cref="EventKind.FromNowOn"/> event — so a BI client can filter/join on "did this
/// event overlap window X" without knowing the per-kind duration rules.
/// </summary>
public sealed class EventFeedItem
{
    /// <summary>Stable identifier; the OData key.</summary>
    public Guid Id { get; set; }

    /// <summary>UTC instant the event occurred, or the start instant for Interval/FromNowOn events.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Short title.</summary>
    public required string Label { get; set; }

    /// <summary>Optional longer free-text description.</summary>
    public string? Description { get; set; }

    /// <summary>How the event relates to time: a single instant, a bounded interval, or an open-ended span.</summary>
    public EventKind Kind { get; set; }

    /// <summary>Duration in whole minutes; only set (and only meaningful) when <see cref="Kind"/> is <c>Interval</c>.</summary>
    public int? DurationMinutes { get; set; }

    /// <summary>
    /// End of the event's span: equal to <see cref="Timestamp"/> for <c>PointInTime</c>,
    /// <c>Timestamp + Duration</c> for <c>Interval</c>, and <c>null</c> (open-ended, runs
    /// indefinitely) for <c>FromNowOn</c>. Filter on this alongside <see cref="Timestamp"/> to find
    /// events overlapping a window regardless of whether their own span is open or closed — see the
    /// worked examples in the PowerBI events-feed reference.
    /// </summary>
    public DateTime? EffectiveEnd { get; set; }

    /// <summary>Services this event affects; empty means "all services".</summary>
    public List<Guid> ServiceIds { get; set; } = new();

    /// <summary>Creation timestamp (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Name of the creator.</summary>
    public string? CreatedBy { get; set; }

    /// <summary>Last update timestamp (UTC).</summary>
    public DateTime ModifiedAt { get; set; }

    /// <summary>Name of the last modifier.</summary>
    public string? ModifiedBy { get; set; }

    /// <summary>Project a domain <see cref="Event"/> onto the feed shape.</summary>
    public static EventFeedItem From(Event e) => new()
    {
        Id = e.Id,
        Timestamp = e.Timestamp,
        Label = e.Label,
        Description = e.Description,
        Kind = e.Kind,
        DurationMinutes = e.Duration is { } d ? (int)Math.Round(d.TotalMinutes) : null,
        EffectiveEnd = e.Kind switch
        {
            EventKind.Interval => e.Timestamp + (e.Duration ?? TimeSpan.Zero),
            EventKind.FromNowOn => null,
            _ => e.Timestamp,
        },
        ServiceIds = e.ServiceIds,
        CreatedAt = e.CreatedAt,
        CreatedBy = e.CreatedBy,
        ModifiedAt = e.ModifiedAt,
        ModifiedBy = e.ModifiedBy,
    };
}
