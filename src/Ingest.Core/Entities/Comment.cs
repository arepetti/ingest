using Ingest.Core.Common;

namespace Ingest.Core.Entities;

/// <summary>
/// The kind of object a <see cref="CommentThread"/> is attached to. Only <see cref="Schema"/>
/// exists today; the type exists so submissions (or other objects) can grow their own comment
/// threads later without reshaping the storage or API.
/// </summary>
public enum CommentTargetType
{
    /// <summary>A schema definition — either the schema as a whole, or one of its values (see <see cref="CommentThread.ValueName"/>).</summary>
    Schema = 0,
}

/// <summary>
/// A single plain-text reply within a <see cref="CommentThread"/>. Embedded in its parent thread's
/// document — comments are always read and written together with the thread they belong to, and
/// volumes are small (admin-curated discussion, not bulk data), so a join collection buys nothing.
/// Deleting a comment soft-deletes it in place so the audit trail and sibling comments stay intact.
/// </summary>
public sealed class Comment
{
    /// <summary>Primary key, unique within the parent thread (and used to address the comment from the API).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Plain-text body.</summary>
    public required string Text { get; set; }

    /// <summary>UTC timestamp at which the comment was posted.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Machine-style account name of the author, for display. <c>null</c> when posted outside an authenticated context.</summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// Id of the authoring account. Used (rather than <see cref="CreatedBy"/>) to decide whether the
    /// current caller "owns" the comment for the self-edit rule, so a later account rename/reuse of
    /// a name can't misattribute ownership.
    /// </summary>
    public Guid? CreatedByAccountId { get; set; }

    /// <summary>UTC timestamp of the last edit. Equals <see cref="CreatedAt"/> until the comment is edited.</summary>
    public DateTime ModifiedAt { get; set; }

    /// <summary>Machine-style account name of the last editor.</summary>
    public string? ModifiedBy { get; set; }

    /// <summary>Soft-deletion flag. Deleted comments are filtered out of every API response.</summary>
    public bool IsDeleted { get; set; }

    /// <summary>UTC timestamp at which the comment was soft-deleted, if it ever was.</summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>Machine-style account name of whoever soft-deleted the comment.</summary>
    public string? DeletedBy { get; set; }
}

/// <summary>
/// A discussion thread attached to a <see cref="CommentTargetType"/> object — for schemas, either
/// the schema as a whole (<see cref="ValueName"/> is <c>null</c>) or one specific
/// <see cref="SchemaValue"/> on it. Holds its own <see cref="Comments"/> embedded, oldest first.
/// <see cref="AuditedEntity.CreatedAt"/>/<see cref="AuditedEntity.CreatedBy"/> mirror the thread's
/// first comment; <see cref="AuditedEntity.ModifiedAt"/>/<see cref="AuditedEntity.ModifiedBy"/>
/// double as "last activity" (bumped by new replies, edits and resolve/reopen).
/// </summary>
public sealed class CommentThread : AuditedEntity
{
    /// <summary>The kind of object this thread is attached to.</summary>
    public CommentTargetType TargetType { get; set; }

    /// <summary>Id of the object this thread is attached to (e.g. the Schema's Id).</summary>
    public Guid TargetId { get; set; }

    /// <summary>
    /// Machine-style <see cref="SchemaValue.Name"/> this thread is scoped to, or <c>null</c> for a
    /// schema-level (general) thread.
    /// </summary>
    public string? ValueName { get; set; }

    /// <summary>
    /// True once the discussion is marked resolved. Resolved threads are locked — no new comments
    /// can be added until a <c>comments:manage</c> holder reopens it.
    /// </summary>
    public bool Resolved { get; set; }

    /// <summary>UTC timestamp of the most recent resolve. Cleared when the thread is reopened.</summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>Machine-style account name of whoever most recently resolved the thread. Cleared when reopened.</summary>
    public string? ResolvedBy { get; set; }

    /// <summary>The thread's replies, oldest first. Soft-deleted comments are retained here but filtered from API responses.</summary>
    public List<Comment> Comments { get; set; } = new();
}
