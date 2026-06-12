namespace Ingest.Infrastructure.Retention;

/// <summary>
/// Binding target for the <c>Retention</c> configuration section. Drives the storage-limitation
/// purge (UK GDPR Art. 5(1)(e)). The whole feature is off by default; each per-target window is a
/// day count where <c>0</c> (or absent) means "keep forever".
/// </summary>
public sealed class RetentionOptions
{
    /// <summary>Master switch. When <c>false</c> (default) no purge runs and the worker isn't registered.</summary>
    public bool Enabled { get; set; }

    /// <summary>How often (hours) the in-process worker runs a purge pass. Floored at 1.</summary>
    public int PollHours { get; set; } = 24;

    /// <summary>Days to keep delivered/failed outbox messages (full-body PII). <c>0</c> = keep forever.</summary>
    public int SentEmailsDays { get; set; }

    /// <summary>Days to keep audit-log entries. <c>0</c> = keep forever.</summary>
    public int AuditLogDays { get; set; }

    /// <summary>Days to keep soft-deleted rows (accounts/schemas/submissions/samples/reports) before hard-deleting them. <c>0</c> = keep forever.</summary>
    public int SoftDeletedDays { get; set; }

    /// <summary>Days to keep notification dedupe markers. <c>0</c> = keep forever.</summary>
    public int NotificationLogDays { get; set; }
}
