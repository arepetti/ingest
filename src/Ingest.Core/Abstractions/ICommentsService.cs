using Ingest.Core.Entities;

namespace Ingest.Core.Abstractions;

/// <summary>
/// CRUD + moderation for comment threads attached to schemas (and, later, other target types). All
/// writes are audited (see <see cref="IAuditLogService"/>); reads never return soft-deleted threads
/// or comments.
/// </summary>
public interface ICommentsService
{
    /// <summary>
    /// Every thread attached to the given target (schema-level and every value-scoped thread
    /// alike), each with its non-deleted comments. Sorted unresolved-first, then by most recent
    /// activity. Unpaged — comment volume is small and admin-curated, like <see cref="Event"/>.
    /// </summary>
    /// <param name="targetType">The kind of object the threads are attached to.</param>
    /// <param name="targetId">Id of the object the threads are attached to.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<CommentThread>> ListThreadsAsync(CommentTargetType targetType, Guid targetId, CancellationToken ct = default);

    /// <summary>
    /// Count open (unresolved, non-deleted) threads per target id, for a batch of targets in one
    /// round trip. Powers the "open threads" column on the schemas list. Targets with zero open
    /// threads are omitted from the result.
    /// </summary>
    /// <param name="targetType">The kind of object the threads are attached to.</param>
    /// <param name="targetIds">The target ids to count for.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyDictionary<Guid, int>> CountOpenThreadsAsync(CommentTargetType targetType, IReadOnlyCollection<Guid> targetIds, CancellationToken ct = default);

    /// <summary>
    /// Start a new thread with its first comment. Validates that the target exists and, when
    /// <paramref name="valueName"/> is supplied, that it names an existing value on the schema.
    /// </summary>
    /// <param name="targetType">The kind of object to attach the thread to.</param>
    /// <param name="targetId">Id of the object to attach the thread to.</param>
    /// <param name="valueName">Machine-style value name to scope the thread to, or <c>null</c> for a schema-level thread.</param>
    /// <param name="text">The first comment's text.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="Common.NotFoundException">No object of that type/id exists.</exception>
    /// <exception cref="Common.ValidationException"><paramref name="valueName"/> doesn't name a value on the schema, or <paramref name="text"/> is blank/too long.</exception>
    Task<CommentThread> CreateThreadAsync(CommentTargetType targetType, Guid targetId, string? valueName, string text, CancellationToken ct = default);

    /// <summary>Append a reply to an existing thread.</summary>
    /// <param name="threadId">Id of the thread to reply to.</param>
    /// <param name="text">The reply's text.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="Common.NotFoundException">No thread with that id exists.</exception>
    /// <exception cref="Common.ConflictException">The thread is resolved (locked) — reopen it first.</exception>
    /// <exception cref="Common.ValidationException"><paramref name="text"/> is blank or too long.</exception>
    Task<CommentThread> AddCommentAsync(Guid threadId, string text, CancellationToken ct = default);

    /// <summary>
    /// Edit a comment's text. Allowed when <paramref name="callerCanManageAny"/> is true (the
    /// caller holds <c>comments:manage</c>), or when the caller is the comment's own author
    /// (<paramref name="callerAccountId"/> matches <see cref="Comment.CreatedByAccountId"/>) — the
    /// caller having reached this method at all already implies it holds <c>comments:create</c>.
    /// </summary>
    /// <param name="commentId">Id of the comment to edit.</param>
    /// <param name="text">The new text.</param>
    /// <param name="callerCanManageAny">Whether the caller holds <c>comments:manage</c>.</param>
    /// <param name="callerAccountId">Id of the calling account, or <c>null</c> outside an authenticated context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="Common.NotFoundException">No comment with that id exists.</exception>
    /// <exception cref="Common.ForbiddenException">The caller neither manages any comment nor owns this one.</exception>
    /// <exception cref="Common.ValidationException"><paramref name="text"/> is blank or too long.</exception>
    Task<CommentThread> EditCommentAsync(Guid commentId, string text, bool callerCanManageAny, Guid? callerAccountId, CancellationToken ct = default);

    /// <summary>Soft-delete a single comment. Callers must hold <c>comments:manage</c> (enforced by the controller).</summary>
    /// <param name="commentId">Id of the comment to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="Common.NotFoundException">No comment with that id exists.</exception>
    Task DeleteCommentAsync(Guid commentId, CancellationToken ct = default);

    /// <summary>
    /// Resolve or reopen a thread. Resolving locks it against new comments; reopening clears the
    /// lock and the resolved-by/at stamp. Callers must hold <c>comments:manage</c> (enforced by the
    /// controller). Recorded in the audit log as an <see cref="AuditChangeType.Edit"/>.
    /// </summary>
    /// <param name="threadId">Id of the thread to resolve/reopen.</param>
    /// <param name="resolved">The new resolved state.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="Common.NotFoundException">No thread with that id exists.</exception>
    Task<CommentThread> ResolveThreadAsync(Guid threadId, bool resolved, CancellationToken ct = default);

    /// <summary>
    /// Soft-delete a thread and every one of its (non-deleted) comments. Callers must hold
    /// <c>comments:manage</c> (enforced by the controller).
    /// </summary>
    /// <param name="threadId">Id of the thread to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="Common.NotFoundException">No thread with that id exists.</exception>
    Task DeleteThreadAsync(Guid threadId, CancellationToken ct = default);
}
