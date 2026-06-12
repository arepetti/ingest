using Ingest.Core.Common;
using Ingest.Core.Entities;

namespace Ingest.Core.Abstractions;

/// <summary>
/// Persistence boundary for <see cref="AuditLog"/> entries. The log is append-only: entries are
/// inserted once and only ever read back (paged, or streamed for export). There is no update or
/// delete path.
/// </summary>
public interface IAuditLogRepository
{
    /// <summary>Append a new audit entry.</summary>
    /// <param name="entry">The entry to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AddAsync(AuditLog entry, CancellationToken ct = default);

    /// <summary>Page through audit entries, newest first, with optional change/target/name filters.</summary>
    /// <param name="request">Paging parameters.</param>
    /// <param name="change">Restrict to a single change type when set.</param>
    /// <param name="targetType">Restrict to a single target type when set.</param>
    /// <param name="nameFilter">Case-insensitive substring matched against either the target or actor name when set.</param>
    /// <param name="from">Lower bound on the entry timestamp (inclusive) when set.</param>
    /// <param name="to">Upper bound on the entry timestamp (exclusive) when set.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A page of entries with the total count.</returns>
    Task<PagedResult<AuditLog>> ListAsync(
        PageRequest request,
        AuditChangeType? change = null,
        AuditTargetType? targetType = null,
        string? nameFilter = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default);

    /// <summary>Page through every entry targeting a single object, newest first.</summary>
    /// <param name="targetId">Id of the object whose history to return.</param>
    /// <param name="request">Paging parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A page of entries with the total count.</returns>
    Task<PagedResult<AuditLog>> ListByTargetAsync(Guid targetId, PageRequest request, CancellationToken ct = default);

    /// <summary>
    /// Stream every matching entry, newest first, without paging. Used by the CSV export so the
    /// whole (optionally filtered) log can be written to the response without buffering it in
    /// memory.
    /// </summary>
    /// <param name="change">Restrict to a single change type when set.</param>
    /// <param name="targetType">Restrict to a single target type when set.</param>
    /// <param name="nameFilter">Case-insensitive substring matched against either the target or actor name when set.</param>
    /// <param name="from">Lower bound on the entry timestamp (inclusive) when set.</param>
    /// <param name="to">Upper bound on the entry timestamp (exclusive) when set.</param>
    /// <param name="ct">Cancellation token.</param>
    IAsyncEnumerable<AuditLog> StreamAsync(
        AuditChangeType? change = null,
        AuditTargetType? targetType = null,
        string? nameFilter = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default);

    /// <summary>
    /// Return every entry in which an account appears as either the target or the actor, newest
    /// first. Used by the DSAR export to surface the audit trail tied to a subject.
    /// </summary>
    /// <param name="accountId">Account id to match against <c>TargetId</c> or <c>ActorId</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The matching entries, newest first.</returns>
    Task<IReadOnlyList<AuditLog>> ListForAccountAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Rewrite the denormalised actor/target names for an account to a pseudonym wherever it
    /// appears as actor or target, preserving the accountability trail without the identity.
    /// Backs the GDPR anonymise path.
    /// </summary>
    /// <param name="accountId">Account id whose name snapshots should be pseudonymised.</param>
    /// <param name="pseudonym">Replacement name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of entries rewritten.</returns>
    Task<long> AnonymiseAccountAsync(Guid accountId, string pseudonym, CancellationToken ct = default);

    /// <summary>
    /// Permanently remove every entry in which an account appears as target or actor. Backs the
    /// GDPR full-delete path.
    /// </summary>
    /// <param name="accountId">Account id to match against <c>TargetId</c> or <c>ActorId</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of entries removed.</returns>
    Task<long> HardDeleteForAccountAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Permanently remove audit entries older than the cutoff. Backs the retention sweep.
    /// </summary>
    /// <param name="olderThanUtc">Entries timestamped before this instant are purged.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of entries removed.</returns>
    Task<long> PurgeOlderThanAsync(DateTime olderThanUtc, CancellationToken ct = default);
}
