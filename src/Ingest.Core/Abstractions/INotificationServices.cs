using Ingest.Core.Entities;

namespace Ingest.Core.Abstractions;

/// <summary>Patch for a single notification trigger.</summary>
/// <param name="Enabled">Master switch for the trigger.</param>
/// <param name="NotifyServiceAccount">Copy the service account's contact email.</param>
/// <param name="NotifyAdminList">Copy the shared admin/operator recipient list.</param>
public sealed record NotificationRuleUpdate(bool Enabled, bool NotifyServiceAccount, bool NotifyAdminList);

/// <summary>Patch for the whole notification configuration.</summary>
/// <param name="Upcoming">Upcoming-reminder rule.</param>
/// <param name="Missed">Missed-alert rule.</param>
/// <param name="Warnings">Warnings-notice rule.</param>
/// <param name="UpcomingLeadHours">Lead time before a window closes that an upcoming reminder fires.</param>
/// <param name="AdminRecipientAccountIds">Accounts that receive the admin-list copy.</param>
public sealed record NotificationSettingsUpdate(
    NotificationRuleUpdate Upcoming,
    NotificationRuleUpdate Missed,
    NotificationRuleUpdate Warnings,
    int UpcomingLeadHours,
    IReadOnlyList<Guid> AdminRecipientAccountIds);

/// <summary>Reads and writes the singleton notification configuration.</summary>
public interface INotificationSettingsService
{
    /// <summary>Get the current notification settings, creating a defaults document if none exists.</summary>
    Task<NotificationSettings> GetAsync(CancellationToken ct = default);

    /// <summary>Apply an admin edit to the notification settings.</summary>
    Task<NotificationSettings> UpdateAsync(NotificationSettingsUpdate update, CancellationToken ct = default);
}

/// <summary>Counts produced by one notification run.</summary>
/// <param name="UpcomingQueued">Emails queued for upcoming reminders.</param>
/// <param name="MissedQueued">Emails queued for missed alerts.</param>
/// <param name="WarningsQueued">Emails queued for warning notices.</param>
public sealed record NotificationRunResult(int UpcomingQueued, int MissedQueued, int WarningsQueued)
{
    /// <summary>Total emails queued across all triggers.</summary>
    public int TotalQueued => UpcomingQueued + MissedQueued + WarningsQueued;
}

/// <summary>
/// Evaluates the enabled notification triggers, renders the matching templates, and enqueues
/// emails into the outbox. Deduplicated so the same window/submission is notified at most once.
/// </summary>
public interface INotificationService
{
    /// <summary>Run every enabled trigger once and enqueue any resulting emails.</summary>
    Task<NotificationRunResult> RunAsync(CancellationToken ct = default);
}

/// <summary>
/// Maintenance boundary for the dedupe markers in the notification log. The notification job
/// itself writes markers directly; this interface exists for the GDPR erasure and retention
/// paths that need to remove them.
/// </summary>
public interface INotificationLogRepository
{
    /// <summary>
    /// Permanently remove every dedupe marker whose key references the given service account.
    /// Marker keys embed the service id (e.g. <c>upcoming:{serviceId}:{schema}:{value}:{period}</c>).
    /// Backs the GDPR full-delete path.
    /// </summary>
    /// <param name="serviceId">Service account id embedded in the marker key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of markers removed.</returns>
    Task<long> HardDeleteForServiceAsync(Guid serviceId, CancellationToken ct = default);

    /// <summary>
    /// Permanently remove dedupe markers created before the cutoff. Backs the retention sweep.
    /// </summary>
    /// <param name="olderThanUtc">Markers created before this instant are purged.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of markers removed.</returns>
    Task<long> PurgeOlderThanAsync(DateTime olderThanUtc, CancellationToken ct = default);
}
