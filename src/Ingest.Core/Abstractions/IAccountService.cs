using Ingest.Core.Common;
using Ingest.Core.Entities;

namespace Ingest.Core.Abstractions;

/// <summary>
/// Patch shape for updating an existing account. Only fields the registry allows to change are
/// here — name and kind are immutable once an account exists.
/// </summary>
/// <param name="Label">New friendly label (null leaves it untouched conceptually; in practice always supplied by the API).</param>
/// <param name="Description">New free-text description.</param>
/// <param name="Role">New role assignment.</param>
/// <param name="Enabled">New enabled flag. Disabling an account immediately invalidates its API keys.</param>
public sealed record AccountUpdate(string? Label, string? Description, AccountRole Role, bool Enabled);

/// <summary>
/// Domain service that owns the lifecycle rules for <see cref="Account"/> aggregates: enforces the
/// uniqueness of the account <c>name</c> (across both live and soft-deleted records), tracks the
/// audit trail through <see cref="IAuditContext"/>, and translates "not found" into <c>null</c>
/// so controllers can map cleanly to <c>404</c>.
/// </summary>
public interface IAccountService
{
    /// <summary>Page through accounts, optionally filtered by kind and/or role.</summary>
    /// <param name="request">Paging and sort parameters.</param>
    /// <param name="kind">When non-null, only accounts of the given kind are returned.</param>
    /// <param name="role">When non-null, only accounts holding the given role are returned.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A page of <see cref="Account"/> records along with the total count.</returns>
    Task<PagedResult<Account>> ListAsync(PageRequest request, AccountKind? kind, AccountRole? role, CancellationToken ct = default);

    /// <summary>Fetch a single account by id.</summary>
    /// <param name="id">Account id.</param>
    /// <param name="includeDeleted">When true, soft-deleted accounts are returned alongside live ones.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The account, or <c>null</c> if no match exists.</returns>
    Task<Account?> GetAsync(Guid id, bool includeDeleted, CancellationToken ct = default);

    /// <summary>Persist a brand-new account, stamping audit fields automatically.</summary>
    /// <param name="input">An <see cref="Account"/> seeded with the request payload. The id is overwritten.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted account (with its assigned id and audit timestamps).</returns>
    /// <exception cref="ConflictException">Another account (including a soft-deleted one) already uses the same name.</exception>
    Task<Account> CreateAsync(Account input, CancellationToken ct = default);

    /// <summary>Update the mutable fields on an existing account.</summary>
    /// <param name="id">Account id.</param>
    /// <param name="update">Patch with the new values.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated account, or <c>null</c> if no live account with that id exists.</returns>
    Task<Account?> UpdateAsync(Guid id, AccountUpdate update, CancellationToken ct = default);

    /// <summary>Soft-delete an account.</summary>
    /// <remarks>The call is idempotent: deleting an already-deleted (or non-existent) account is a no-op.</remarks>
    /// <param name="id">Account id.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
