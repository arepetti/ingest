using Ingest.Core.Common;

namespace Ingest.Core.Entities;

/// <summary>The three notification triggers an admin can independently switch on.</summary>
public enum NotificationKind
{
    /// <summary>A required value's cadence window is about to close and nothing has been submitted yet.</summary>
    Upcoming = 0,

    /// <summary>A required value's previous cadence window closed unsatisfied (the deadline passed).</summary>
    Missed = 1,

    /// <summary>A submission was accepted but carried validation warnings worth a human's attention.</summary>
    Warnings = 2,

    /// <summary>A submission was accepted but is held awaiting approval before it goes live.</summary>
    PendingApproval = 3,

    /// <summary>A pending submission was approved and is now live.</summary>
    Approved = 4,

    /// <summary>A pending submission was rejected and will not go live.</summary>
    Rejected = 5,

    /// <summary>A submission was saved as a work-in-progress draft (fired on every draft save).</summary>
    DraftSaved = 6,
}

/// <summary>
/// Per-trigger configuration: whether the trigger is on, and who receives it. The two recipient
/// switches are additive — a notification can go to the service's own contact email, to the
/// shared admin/operator recipient list, or both.
/// </summary>
public sealed class NotificationRule
{
    /// <summary>Master switch for this trigger.</summary>
    public bool Enabled { get; set; }

    /// <summary>Send to the contact email on the service account the notification is about.</summary>
    public bool NotifyServiceAccount { get; set; } = true;

    /// <summary>Send to every account in <see cref="NotificationSettings.AdminRecipientAccountIds"/>.</summary>
    public bool NotifyAdminList { get; set; }
}

/// <summary>
/// Singleton document holding the notification configuration. The notification job reads it on
/// every run; the Settings page edits it. Defaults are conservative — every trigger is off until
/// an admin enables it.
/// </summary>
public sealed class NotificationSettings : AuditedEntity
{
    /// <summary>Rule for the "upcoming submission" reminder.</summary>
    public NotificationRule Upcoming { get; set; } = new();

    /// <summary>Rule for the "missed submission" alert.</summary>
    public NotificationRule Missed { get; set; } = new();

    /// <summary>Rule for the "submission with warnings" notice.</summary>
    public NotificationRule Warnings { get; set; } = new();

    /// <summary>
    /// Rule for the "submission pending approval" notice. When enabled, the submission's designated
    /// approvers are always emailed; the two recipient switches add the submitter and/or admin list.
    /// </summary>
    public NotificationRule PendingApproval { get; set; } = new();

    /// <summary>Rule for the "submission approved" notice (the submission is now live).</summary>
    public NotificationRule Approved { get; set; } = new();

    /// <summary>Rule for the "submission rejected" notice (carries the reviewer's reason, if any).</summary>
    public NotificationRule Rejected { get; set; } = new();

    /// <summary>
    /// Rule for the "draft saved" nudge. Fires on every draft save (no dedupe). The default recipient
    /// is the submission's service-account contact — the people filling the values in — with optional
    /// admin/operator copies. Independent of the approval feature.
    /// </summary>
    public NotificationRule DraftSaved { get; set; } = new();

    /// <summary>
    /// How many hours before a cadence window closes an "upcoming" reminder should fire. A value
    /// is reminded once per window when it enters this lead time still unsatisfied.
    /// </summary>
    public int UpcomingLeadHours { get; set; } = 24;

    /// <summary>Accounts (admins/operators) that receive a copy when a rule has <see cref="NotificationRule.NotifyAdminList"/> on.</summary>
    public List<Guid> AdminRecipientAccountIds { get; set; } = new();
}

/// <summary>
/// Dedupe marker written after a notification event has been turned into queued email(s). The
/// notification job checks for a matching <see cref="Key"/> before acting so the same window /
/// submission is never notified twice across runs.
/// </summary>
public sealed class NotificationLog : AuditedEntity
{
    /// <summary>Deterministic event key, e.g. <c>upcoming:{serviceId}:{schema}:{value}:{periodStart:o}</c>. Unique.</summary>
    public required string Key { get; set; }

    /// <summary>Which trigger produced the event (for diagnostics/cleanup).</summary>
    public NotificationKind Kind { get; set; }
}
