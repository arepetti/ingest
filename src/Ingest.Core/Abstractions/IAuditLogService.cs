using Ingest.Core.Common;
using Ingest.Core.Entities;

namespace Ingest.Core.Abstractions;

/// <summary>
/// Application-facing facade over the audit log. The other services call <see cref="RecordAsync"/>
/// after a successful create/edit/delete; the audit controllers use the query methods. The actor
/// and timestamp are resolved from the ambient <see cref="IAuditContext"/>, so callers only supply
/// the "what" of the change.
/// </summary>
public interface IAuditLogService
{
    /// <summary>Record a change. The actor (id + name) and timestamp are filled from the current request context.</summary>
    /// <param name="targetType">The type of object that changed.</param>
    /// <param name="change">The kind of change.</param>
    /// <param name="targetId">Id of the changed object.</param>
    /// <param name="targetName">Name of the changed object when it has one; otherwise <c>null</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordAsync(
        AuditTargetType targetType,
        AuditChangeType change,
        Guid targetId,
        string? targetName,
        CancellationToken ct = default);

    /// <summary>
    /// Record a change with an extra free-form note (e.g. a submission rejection reason). Same as
    /// <see cref="RecordAsync(AuditTargetType,AuditChangeType,Guid,string,CancellationToken)"/> but
    /// stamps <see cref="AuditLog.Note"/>.
    /// </summary>
    /// <param name="targetType">The type of object that changed.</param>
    /// <param name="change">The kind of change.</param>
    /// <param name="targetId">Id of the changed object.</param>
    /// <param name="targetName">Name of the changed object when it has one; otherwise <c>null</c>.</param>
    /// <param name="note">Free-form context to store on the entry.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordAsync(
        AuditTargetType targetType,
        AuditChangeType change,
        Guid targetId,
        string? targetName,
        string? note,
        CancellationToken ct = default);

    /// <summary>Page through the log, newest first, with optional change/target/name/timestamp filters.</summary>
    Task<PagedResult<AuditLog>> ListAsync(
        PageRequest request,
        AuditChangeType? change = null,
        AuditTargetType? targetType = null,
        string? nameFilter = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default);

    /// <summary>Page through every entry targeting a single object, newest first.</summary>
    Task<PagedResult<AuditLog>> ListByTargetAsync(Guid targetId, PageRequest request, CancellationToken ct = default);

    /// <summary>Stream every matching entry, newest first, for export.</summary>
    IAsyncEnumerable<AuditLog> StreamAsync(
        AuditChangeType? change = null,
        AuditTargetType? targetType = null,
        string? nameFilter = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default);
}
