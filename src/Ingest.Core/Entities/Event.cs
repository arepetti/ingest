using Ingest.Core.Common;

namespace Ingest.Core.Entities;

/// <summary>
/// Distinguishes how an <see cref="Event"/> relates to time: a single instant, a bounded span with
/// a known end, or an open-ended span that starts at <see cref="Event.Timestamp"/> and runs
/// indefinitely.
/// </summary>
public enum EventKind
{
    /// <summary>A single instant — no duration.</summary>
    PointInTime = 0,

    /// <summary>A bounded span; <see cref="Event.Duration"/> is required.</summary>
    Interval = 1,

    /// <summary>An open-ended span starting at <see cref="Event.Timestamp"/> with no known end.</summary>
    FromNowOn = 2,
}

/// <summary>
/// A point-in-time occurrence (e.g. a maintenance window, an incident, a deployment) recorded by an
/// admin, optionally scoped to the services it affects. Purely informational — events don't drive
/// any validation or notification pipeline; they exist for admins/operators to annotate the
/// timeline. Soft-deleted so audit history is preserved.
/// </summary>
public sealed class Event : AuditedEntity
{
    /// <summary>UTC instant the event occurred, or the start instant for <see cref="EventKind.Interval"/>/<see cref="EventKind.FromNowOn"/> events.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Short required title shown in the events list.</summary>
    public required string Label { get; set; }

    /// <summary>Optional longer free-text description.</summary>
    public string? Description { get; set; }

    /// <summary>How this event relates to time. Defaults to a single instant.</summary>
    public EventKind Kind { get; set; } = EventKind.PointInTime;

    /// <summary>
    /// Span of the event from <see cref="Timestamp"/>. Required (and must be positive) when
    /// <see cref="Kind"/> is <see cref="EventKind.Interval"/>; ignored/cleared otherwise.
    /// </summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>Services this event affects. Empty means "all services".</summary>
    public List<Guid> ServiceIds { get; set; } = new();
}
