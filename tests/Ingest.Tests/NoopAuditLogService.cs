using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;

namespace Ingest.Tests;

/// <summary>
/// No-op <see cref="IAuditLogService"/> for unit tests that exercise the domain services without
/// caring about the audit trail. Records nothing and returns empty results.
/// </summary>
internal sealed class NoopAuditLogService : IAuditLogService
{
    public Task RecordAsync(AuditTargetType targetType, AuditChangeType change, Guid targetId, string? targetName, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task RecordAsync(AuditTargetType targetType, AuditChangeType change, Guid targetId, string? targetName, string? note, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<PagedResult<AuditLog>> ListAsync(PageRequest request, AuditChangeType? change = null, AuditTargetType? targetType = null, string? nameFilter = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default) =>
        Task.FromResult(new PagedResult<AuditLog>(Array.Empty<AuditLog>(), 0, request.Page, request.PageSize));

    public Task<PagedResult<AuditLog>> ListByTargetAsync(Guid targetId, PageRequest request, CancellationToken ct = default) =>
        Task.FromResult(new PagedResult<AuditLog>(Array.Empty<AuditLog>(), 0, request.Page, request.PageSize));

    public async IAsyncEnumerable<AuditLog> StreamAsync(AuditChangeType? change = null, AuditTargetType? targetType = null, string? nameFilter = null, DateTime? from = null, DateTime? to = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}
