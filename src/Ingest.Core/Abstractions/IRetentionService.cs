namespace Ingest.Core.Abstractions;

/// <summary>Per-target tally of what one retention purge pass removed.</summary>
/// <param name="EmailsPurged">Delivered/failed outbox messages removed.</param>
/// <param name="SoftDeletedPurged">Soft-deleted rows (across all collections) hard-deleted.</param>
/// <param name="AuditEntriesPurged">Audit-log entries removed.</param>
/// <param name="NotificationMarkersPurged">Notification dedupe markers removed.</param>
public sealed record RetentionRunResult(
    long EmailsPurged,
    long SoftDeletedPurged,
    long AuditEntriesPurged,
    long NotificationMarkersPurged)
{
    /// <summary>Total documents removed across every target.</summary>
    public long Total => EmailsPurged + SoftDeletedPurged + AuditEntriesPurged + NotificationMarkersPurged;
}

/// <summary>
/// Enforces the configured retention windows by hard-deleting data that has outlived them. Backs
/// both the in-process <c>RetentionWorker</c> and the manual <c>POST /api/admin/retention/run</c>
/// trigger. Targets whose configured window is <c>0</c> (keep forever) are skipped.
/// </summary>
public interface IRetentionService
{
    /// <summary>Run one purge pass across every configured target and report what was removed.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A tally of removed documents per target.</returns>
    Task<RetentionRunResult> PurgeAsync(CancellationToken ct = default);
}
