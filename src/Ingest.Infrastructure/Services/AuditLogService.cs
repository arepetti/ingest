using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;

namespace Ingest.Infrastructure.Services;

/// <summary>
/// Default implementation of <see cref="IAuditLogService"/>. Stamps each recorded change with the
/// current actor (id + machine name) and timestamp from the ambient <see cref="IAuditContext"/>,
/// then delegates persistence and querying to <see cref="IAuditLogRepository"/>.
/// </summary>
public sealed class AuditLogService : IAuditLogService
{
    private readonly IAuditLogRepository _repo;
    private readonly IAuditContext _audit;

    /// <summary>Create a new <see cref="AuditLogService"/>.</summary>
    /// <param name="repo">Audit-log repository.</param>
    /// <param name="audit">Ambient who/when context.</param>
    public AuditLogService(IAuditLogRepository repo, IAuditContext audit)
    {
        _repo = repo;
        _audit = audit;
    }

    /// <inheritdoc />
    public Task RecordAsync(
        AuditTargetType targetType,
        AuditChangeType change,
        Guid targetId,
        string? targetName,
        CancellationToken ct = default) =>
        RecordAsync(targetType, change, targetId, targetName, note: null, ct);

    /// <inheritdoc />
    public Task RecordAsync(
        AuditTargetType targetType,
        AuditChangeType change,
        Guid targetId,
        string? targetName,
        string? note,
        CancellationToken ct = default)
    {
        var entry = new AuditLog
        {
            Timestamp = _audit.UtcNow,
            TargetType = targetType,
            Change = change,
            TargetId = targetId,
            TargetName = targetName,
            ActorId = _audit.AccountId,
            ActorName = _audit.UserName,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };
        return _repo.AddAsync(entry, ct);
    }

    /// <inheritdoc />
    public Task<PagedResult<AuditLog>> ListAsync(
        PageRequest request,
        AuditChangeType? change = null,
        AuditTargetType? targetType = null,
        string? nameFilter = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default) =>
        _repo.ListAsync(request, change, targetType, nameFilter, from, to, ct);

    /// <inheritdoc />
    public Task<PagedResult<AuditLog>> ListByTargetAsync(Guid targetId, PageRequest request, CancellationToken ct = default) =>
        _repo.ListByTargetAsync(targetId, request, ct);

    /// <inheritdoc />
    public IAsyncEnumerable<AuditLog> StreamAsync(
        AuditChangeType? change = null,
        AuditTargetType? targetType = null,
        string? nameFilter = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default) =>
        _repo.StreamAsync(change, targetType, nameFilter, from, to, ct);
}
