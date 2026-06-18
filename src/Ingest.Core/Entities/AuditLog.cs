namespace Ingest.Core.Entities;

/// <summary>
/// The kind of lifecycle change recorded in the audit log. The changed values themselves are not
/// stored — only that the change happened, who did it, and to what.
/// </summary>
public enum AuditChangeType
{
    /// <summary>A new object was created.</summary>
    Create = 0,

    /// <summary>An existing object was modified.</summary>
    Edit = 1,

    /// <summary>An object was deleted (or, for API keys which cannot be deleted, revoked).</summary>
    Delete = 2,

    /// <summary>A submission was approved (an approval-workflow decision). Recorded distinctly so the audit log is filterable.</summary>
    Approve = 3,

    /// <summary>A submission was rejected (an approval-workflow decision). The reason, when supplied, is stored in <see cref="AuditLog.Note"/>.</summary>
    Reject = 4,
}

/// <summary>
/// The type of object an audit entry targets. Lets a reader locate the object behind
/// <see cref="AuditLog.TargetId"/> without scanning every collection. <see cref="User"/> and
/// <see cref="Account"/> are both backed by the accounts collection; they are distinguished by the
/// account's <see cref="AccountKind"/> at the time of the change.
/// </summary>
public enum AuditTargetType
{
    /// <summary>An interactive (<see cref="AccountKind.User"/>) account.</summary>
    User = 0,

    /// <summary>An API-only (<see cref="AccountKind.Application"/>) account.</summary>
    Account = 1,

    /// <summary>A schema definition.</summary>
    Schema = 2,

    /// <summary>An API key.</summary>
    ApiKey = 3,

    /// <summary>A submission.</summary>
    Submission = 4,

    /// <summary>A report.</summary>
    Report = 5,

    /// <summary>
    /// A schema version-history snapshot (an entry in the schema's saved version history). Distinct
    /// from <see cref="Schema"/> so cleaning up history entries isn't mistaken for deleting the
    /// live schema itself.
    /// </summary>
    SchemaHistory = 6,

    /// <summary>A cross-cutting approval rule (per-service/per-schema approval requirement).</summary>
    ApprovalRule = 7,
}

/// <summary>
/// An append-only record of a single create/edit/delete change applied to a domain object. Stored
/// in its own <c>auditLogs</c> collection and never updated or soft-deleted. Carries enough
/// denormalised context (names + ids of both the target and the actor, plus a target type) for the
/// audit UI to render rows and locate objects without joining back to their source collections.
/// </summary>
public sealed class AuditLog
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>UTC timestamp at which the change occurred.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>The type of object that was changed.</summary>
    public AuditTargetType TargetType { get; set; }

    /// <summary>Id of the object that was changed.</summary>
    public Guid TargetId { get; set; }

    /// <summary>Name of the changed object when it has one (e.g. account/schema/report name, or an API key's key id). <c>null</c> for nameless objects.</summary>
    public string? TargetName { get; set; }

    /// <summary>The kind of change.</summary>
    public AuditChangeType Change { get; set; }

    /// <summary>Id of the account that performed the change, or <c>null</c> when acted outside an authenticated context.</summary>
    public Guid? ActorId { get; set; }

    /// <summary>Machine name of the account that performed the change, or <c>null</c> as above.</summary>
    public string? ActorName { get; set; }

    /// <summary>
    /// Optional free-form note carrying extra context for the change — e.g. the reason supplied
    /// when a submission is rejected. General-purpose; <c>null</c> for changes that don't need it.
    /// Legacy documents that predate this field deserialize to <c>null</c>.
    /// </summary>
    public string? Note { get; set; }
}
