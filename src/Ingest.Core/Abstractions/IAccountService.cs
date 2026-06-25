using Ingest.Core.Common;
using Ingest.Core.Entities;

namespace Ingest.Core.Abstractions;

/// <summary>
/// Patch shape for updating an existing account. Only fields the registry allows to change are
/// here — name and kind are immutable once an account exists.
/// </summary>
/// <param name="Label">New friendly label (null leaves it untouched conceptually; in practice always supplied by the API).</param>
/// <param name="Description">New free-text description.</param>
/// <param name="Email">New contact email; blank/null clears it. Format-validated (when non-empty) by the service.</param>
/// <param name="Role">New role assignment.</param>
/// <param name="Enabled">New enabled flag. Disabling an account immediately invalidates its API keys.</param>
/// <param name="ExternalLogins">Replacement set of SSO identity links, or <c>null</c> to leave the existing links untouched. An empty list clears them. Subjects already bound to surviving links are preserved by the service.</param>
/// <param name="Capabilities">Replacement capability override set, or <c>null</c> to leave the stored overrides untouched. An empty list clears them (the account reverts to its role default bundle); a non-empty list replaces them. Validated (unknown names rejected) and ignored for Admins.</param>
/// <param name="AssignedServiceIds">Replacement assigned-service allowlist, or <c>null</c> to leave it untouched. An empty list clears it (the account becomes unrestricted, seeing every service); a non-empty list confines every cross-service read to those services. Ignored for Admins.</param>
public sealed record AccountUpdate(string? Label, string? Description, string? Email, AccountRole Role, bool Enabled, IReadOnlyList<ExternalLogin>? ExternalLogins = null, IReadOnlyList<string>? Capabilities = null, IReadOnlyList<Guid>? AssignedServiceIds = null);

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

    /// <summary>Project every live account onto the portable, secret-free <see cref="AccountBackupEntry"/> shape for export.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One entry per live account, ordered by name.</returns>
    Task<IReadOnlyList<AccountBackupEntry>> ExportAsync(CancellationToken ct = default);

    /// <summary>
    /// Import a batch of accounts, matching on <see cref="AccountBackupEntry.Name"/>: existing
    /// accounts are updated in place and unknown names are created. API keys are never part of this
    /// data, so created accounts start without any and must have one generated afterwards. Each
    /// entry is applied independently — a failing entry is reported and skipped without aborting the
    /// rest.
    /// </summary>
    /// <param name="accounts">The accounts to upsert.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Counts of created/updated accounts plus any per-entry errors.</returns>
    Task<AccountsImportResult> ImportAsync(IReadOnlyList<AccountBackupEntry> accounts, CancellationToken ct = default);
}
