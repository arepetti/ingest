namespace Ingest.Infrastructure.Email;

/// <summary>
/// Binding target for the <c>Notifications</c> configuration section. Only hosting-level concerns
/// live here (the scheduler cadence); <em>what</em> to notify and <em>who</em> to notify is admin
/// data stored in the database (<see cref="Core.Entities.NotificationSettings"/>).
/// </summary>
public sealed class NotificationOptions
{
    /// <summary>In-process scheduler settings.</summary>
    public NotificationSchedulerOptions Scheduler { get; set; } = new();
}

/// <summary>Controls the background service that periodically runs the notification job.</summary>
public sealed class NotificationSchedulerOptions
{
    /// <summary>
    /// When <c>true</c> (default) an in-process scheduler runs the notification job on a timer.
    /// Set <c>false</c> to drive runs from an external scheduler hitting
    /// <c>POST /api/admin/notifications/run</c>, so scheduling can later be its own service.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often (minutes) the in-process scheduler triggers a notification run.</summary>
    public int PollMinutes { get; set; } = 15;
}
