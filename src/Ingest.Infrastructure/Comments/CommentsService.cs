using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Comments;

/// <summary>
/// Database-backed CRUD + moderation for <see cref="CommentThread"/>s. Threads are stored whole
/// (comments embedded) and always fetched/replaced as a single document — volumes are small and
/// admin-curated, so there's no need for per-comment array updates. Every write is audited; reads
/// filter out soft-deleted threads/comments.
/// </summary>
public sealed class CommentsService : ICommentsService
{
    private const int MaxTextLength = 4000;

    private readonly MongoContext _ctx;
    private readonly ISchemaService _schemas;
    private readonly IAuditLogService _audit;
    private readonly IAuditContext _auditContext;

    /// <summary>Create a new <see cref="CommentsService"/>.</summary>
    public CommentsService(MongoContext ctx, ISchemaService schemas, IAuditLogService audit, IAuditContext auditContext)
    {
        _ctx = ctx;
        _schemas = schemas;
        _audit = audit;
        _auditContext = auditContext;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommentThread>> ListThreadsAsync(CommentTargetType targetType, Guid targetId, CancellationToken ct = default)
    {
        var filter = Builders<CommentThread>.Filter.And(
            Builders<CommentThread>.Filter.Eq(t => t.TargetType, targetType),
            Builders<CommentThread>.Filter.Eq(t => t.TargetId, targetId),
            Builders<CommentThread>.Filter.Eq(t => t.IsDeleted, false));

        var threads = await _ctx.CommentThreads.Find(filter).ToListAsync(ct);

        return threads
            .Select(VisibleOnly)
            .OrderBy(t => t.Resolved)
            .ThenByDescending(t => t.ModifiedAt)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, int>> CountOpenThreadsAsync(CommentTargetType targetType, IReadOnlyCollection<Guid> targetIds, CancellationToken ct = default)
    {
        if (targetIds.Count == 0)
            return new Dictionary<Guid, int>();

        var filter = Builders<CommentThread>.Filter.And(
            Builders<CommentThread>.Filter.Eq(t => t.TargetType, targetType),
            Builders<CommentThread>.Filter.In(t => t.TargetId, targetIds),
            Builders<CommentThread>.Filter.Eq(t => t.Resolved, false),
            Builders<CommentThread>.Filter.Eq(t => t.IsDeleted, false));

        var groups = await _ctx.CommentThreads.Aggregate()
            .Match(filter)
            .Group(t => t.TargetId, g => new { TargetId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return groups.ToDictionary(g => g.TargetId, g => g.Count);
    }

    /// <inheritdoc />
    public async Task<CommentThread> CreateThreadAsync(CommentTargetType targetType, Guid targetId, string? valueName, string text, CancellationToken ct = default)
    {
        var normalizedText = NormalizeText(text);
        var normalizedValueName = string.IsNullOrWhiteSpace(valueName) ? null : valueName;
        var schemaName = await ValidateTargetAsync(targetType, targetId, normalizedValueName, ct);

        var now = _auditContext.UtcNow;
        var comment = NewComment(normalizedText, now);
        var thread = new CommentThread
        {
            TargetType = targetType,
            TargetId = targetId,
            ValueName = normalizedValueName,
            CreatedAt = now,
            CreatedBy = _auditContext.UserName,
            ModifiedAt = now,
            ModifiedBy = _auditContext.UserName,
            Comments = new List<Comment> { comment },
        };

        await _ctx.CommentThreads.InsertOneAsync(thread, cancellationToken: ct);
        await _audit.RecordAsync(AuditTargetType.CommentThread, AuditChangeType.Create, thread.Id, BuildTargetName(schemaName, normalizedValueName), ct);
        return VisibleOnly(thread);
    }

    /// <inheritdoc />
    public async Task<CommentThread> AddCommentAsync(Guid threadId, string text, CancellationToken ct = default)
    {
        var normalizedText = NormalizeText(text);
        var thread = await GetRawThreadOrThrowAsync(threadId, ct);

        if (thread.Resolved)
            throw new ConflictException(Diagnostic.Create(
                DiagnosticCodes.Comments.ThreadResolved,
                "This thread is resolved — reopen it to add another comment.",
                ("threadId", threadId)));

        var now = _auditContext.UtcNow;
        var comment = NewComment(normalizedText, now);
        thread.Comments.Add(comment);
        thread.ModifiedAt = now;
        thread.ModifiedBy = _auditContext.UserName;

        await ReplaceAsync(thread, ct);
        await _audit.RecordAsync(AuditTargetType.Comment, AuditChangeType.Create, comment.Id, await BuildTargetNameAsync(thread, ct), ct);
        return VisibleOnly(thread);
    }

    /// <inheritdoc />
    public async Task<CommentThread> EditCommentAsync(Guid commentId, string text, bool callerCanManageAny, Guid? callerAccountId, CancellationToken ct = default)
    {
        var normalizedText = NormalizeText(text);
        var thread = await GetRawThreadByCommentIdOrThrowAsync(commentId, ct);
        var comment = thread.Comments.FirstOrDefault(c => c.Id == commentId && !c.IsDeleted)
            ?? throw new NotFoundException("Comment");

        if (!callerCanManageAny && comment.CreatedByAccountId != callerAccountId)
            throw new ForbiddenException(Diagnostic.Create(
                DiagnosticCodes.Comments.EditForbidden,
                "You can only edit your own comments.",
                ("commentId", commentId),
                ("authorAccountId", comment.CreatedByAccountId),
                ("callerAccountId", callerAccountId)));

        var now = _auditContext.UtcNow;
        comment.Text = normalizedText;
        comment.ModifiedAt = now;
        comment.ModifiedBy = _auditContext.UserName;
        thread.ModifiedAt = now;
        thread.ModifiedBy = _auditContext.UserName;

        await ReplaceAsync(thread, ct);
        await _audit.RecordAsync(AuditTargetType.Comment, AuditChangeType.Edit, comment.Id, await BuildTargetNameAsync(thread, ct), ct);
        return VisibleOnly(thread);
    }

    /// <inheritdoc />
    public async Task DeleteCommentAsync(Guid commentId, CancellationToken ct = default)
    {
        var thread = await GetRawThreadByCommentIdOrThrowAsync(commentId, ct);
        var comment = thread.Comments.FirstOrDefault(c => c.Id == commentId && !c.IsDeleted)
            ?? throw new NotFoundException("Comment");

        var now = _auditContext.UtcNow;
        comment.IsDeleted = true;
        comment.DeletedAt = now;
        comment.DeletedBy = _auditContext.UserName;
        thread.ModifiedAt = now;
        thread.ModifiedBy = _auditContext.UserName;

        await ReplaceAsync(thread, ct);
        await _audit.RecordAsync(AuditTargetType.Comment, AuditChangeType.Delete, comment.Id, await BuildTargetNameAsync(thread, ct), ct);
    }

    /// <inheritdoc />
    public async Task<CommentThread> ResolveThreadAsync(Guid threadId, bool resolved, CancellationToken ct = default)
    {
        var thread = await GetRawThreadOrThrowAsync(threadId, ct);
        var now = _auditContext.UtcNow;

        thread.Resolved = resolved;
        thread.ResolvedAt = resolved ? now : null;
        thread.ResolvedBy = resolved ? _auditContext.UserName : null;
        thread.ModifiedAt = now;
        thread.ModifiedBy = _auditContext.UserName;

        await ReplaceAsync(thread, ct);
        // Resolve/reopen is a status change on the thread itself, not a new/changed comment — audited as an Edit per spec.
        await _audit.RecordAsync(AuditTargetType.CommentThread, AuditChangeType.Edit, thread.Id, await BuildTargetNameAsync(thread, ct), ct);
        return VisibleOnly(thread);
    }

    /// <inheritdoc />
    public async Task DeleteThreadAsync(Guid threadId, CancellationToken ct = default)
    {
        var thread = await GetRawThreadOrThrowAsync(threadId, ct);
        var now = _auditContext.UtcNow;

        thread.IsDeleted = true;
        thread.DeletedAt = now;
        thread.DeletedBy = _auditContext.UserName;
        thread.ModifiedAt = now;
        thread.ModifiedBy = _auditContext.UserName;
        foreach (var comment in thread.Comments.Where(c => !c.IsDeleted))
        {
            comment.IsDeleted = true;
            comment.DeletedAt = now;
            comment.DeletedBy = _auditContext.UserName;
        }

        await ReplaceAsync(thread, ct);
        await _audit.RecordAsync(AuditTargetType.CommentThread, AuditChangeType.Delete, thread.Id, await BuildTargetNameAsync(thread, ct), ct);
    }

    private Task ReplaceAsync(CommentThread thread, CancellationToken ct) =>
        _ctx.CommentThreads.ReplaceOneAsync(Builders<CommentThread>.Filter.Eq(t => t.Id, thread.Id), thread, cancellationToken: ct);

    private async Task<CommentThread> GetRawThreadOrThrowAsync(Guid threadId, CancellationToken ct)
    {
        var filter = Builders<CommentThread>.Filter.And(
            Builders<CommentThread>.Filter.Eq(t => t.Id, threadId),
            Builders<CommentThread>.Filter.Eq(t => t.IsDeleted, false));
        return await _ctx.CommentThreads.Find(filter).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Comment thread");
    }

    /// <summary>Locate the (non-deleted) thread containing a given comment id. The comment itself may still turn out to be soft-deleted — callers check that separately.</summary>
    private async Task<CommentThread> GetRawThreadByCommentIdOrThrowAsync(Guid commentId, CancellationToken ct)
    {
        var filter = Builders<CommentThread>.Filter.And(
            Builders<CommentThread>.Filter.Eq(t => t.IsDeleted, false),
            Builders<CommentThread>.Filter.ElemMatch(t => t.Comments, c => c.Id == commentId));
        return await _ctx.CommentThreads.Find(filter).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Comment");
    }

    private Comment NewComment(string text, DateTime now) => new()
    {
        Text = text,
        CreatedAt = now,
        CreatedBy = _auditContext.UserName,
        CreatedByAccountId = _auditContext.AccountId,
        ModifiedAt = now,
        ModifiedBy = _auditContext.UserName,
    };

    /// <summary>
    /// Confirms the target (and, when supplied, the value name) actually exist. Returns the
    /// target's display name (the schema's <see cref="Schema.Name"/>) for the audit entry.
    /// </summary>
    private async Task<string?> ValidateTargetAsync(CommentTargetType targetType, Guid targetId, string? valueName, CancellationToken ct)
    {
        switch (targetType)
        {
            case CommentTargetType.Schema:
                var schema = await _schemas.GetByIdAsync(targetId, includeDeleted: false, ct)
                    ?? throw new NotFoundException("Schema");
                if (valueName is not null && !schema.Values.Any(v => v.Name == valueName))
                    throw new ValidationException(new[]
                    {
                        Diagnostic.Create(
                            DiagnosticCodes.Comments.ValueNotOnSchema,
                            $"'{valueName}' is not a value on this schema.",
                            ("schemaId", targetId),
                            ("valueName", valueName)),
                    });
                return schema.Name;
            default:
                throw new ValidationException(new[]
                {
                    Diagnostic.Create(
                        DiagnosticCodes.Comments.TargetTypeUnsupported,
                        "Unsupported comment target type.",
                        ("targetType", targetType.ToString()),
                        ("targetId", targetId)),
                });
        }
    }

    /// <summary>Best-effort human-readable name for a thread, used as the audit entry's <c>TargetName</c>. Falls back to the raw target id if the target can no longer be resolved (e.g. the schema was later hard-removed).</summary>
    private async Task<string?> BuildTargetNameAsync(CommentThread thread, CancellationToken ct)
    {
        string? baseName = thread.TargetType switch
        {
            CommentTargetType.Schema => (await _schemas.GetByIdAsync(thread.TargetId, includeDeleted: true, ct))?.Name,
            _ => null,
        };
        return BuildTargetName(baseName, thread.ValueName);
    }

    private static string BuildTargetName(string? baseName, string? valueName)
    {
        baseName ??= "?";
        return valueName is null ? baseName : $"{baseName} / {valueName}";
    }

    /// <summary>Strips soft-deleted comments and orders the rest oldest-first. Safe to call after a write completes — it only affects the in-memory copy being returned, not what was persisted.</summary>
    private static CommentThread VisibleOnly(CommentThread thread)
    {
        thread.Comments = thread.Comments.Where(c => !c.IsDeleted).OrderBy(c => c.CreatedAt).ToList();
        return thread;
    }

    private static string NormalizeText(string? text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new ValidationException(new[]
            {
                new Diagnostic(DiagnosticCodes.Comments.TextRequired, "Comment text is required."),
            });
        if (trimmed.Length > MaxTextLength)
            throw new ValidationException(new[]
            {
                Diagnostic.Create(
                    DiagnosticCodes.Comments.TextTooLong,
                    $"Comment text exceeds the {MaxTextLength}-character limit.",
                    ("maxLength", MaxTextLength),
                    ("actualLength", trimmed.Length)),
            });
        return trimmed;
    }
}
