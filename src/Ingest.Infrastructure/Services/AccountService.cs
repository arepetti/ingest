using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;

namespace Ingest.Infrastructure.Services;

/// <summary>
/// Default implementation of <see cref="IAccountService"/>. Owns the uniqueness rule on account
/// names (checked against soft-deleted rows too) and the "update only the mutable fields"
/// behaviour expected by callers. Pure repository forwarding for the rest.
/// </summary>
public sealed class AccountService : IAccountService
{
    private readonly IAccountRepository _accounts;
    private readonly ISampleRepository _samples;

    /// <summary>Create a new <see cref="AccountService"/>.</summary>
    /// <param name="accounts">Account repository.</param>
    /// <param name="samples">Sample projection repository (used by the delete guard to detect accounts that already own historical data).</param>
    public AccountService(IAccountRepository accounts, ISampleRepository samples)
    {
        _accounts = accounts;
        _samples = samples;
    }

    /// <inheritdoc />
    public Task<PagedResult<Account>> ListAsync(PageRequest request, AccountKind? kind, AccountRole? role, CancellationToken ct = default) =>
        _accounts.ListAsync(request, kind, role, ct);

    /// <inheritdoc />
    public Task<Account?> GetAsync(Guid id, bool includeDeleted, CancellationToken ct = default) =>
        _accounts.GetByIdAsync(id, includeDeleted, ct);

    /// <inheritdoc />
    public async Task<Account> CreateAsync(Account input, CancellationToken ct = default)
    {
        // Soft-deleted accounts still hold their name slot in the unique index. If the caller is
        // recreating an account that was previously deleted, hard-delete the old row first so the
        // fresh insert can take the slot. The samples that originated from the deleted account
        // remain in place but are filtered out of every read path by their own IsDeleted flag —
        // and that flag was set when the account was deleted (delete is now refused while live
        // samples still reference an account, so by the time we get here those samples have
        // been removed too).
        var collision = await _accounts.GetByNameAsync(input.Name, includeDeleted: true, ct);
        if (collision is not null)
        {
            if (!collision.IsDeleted)
                throw new ConflictException($"Account '{input.Name}' already exists.");

            await _accounts.HardDeleteAsync(collision.Id, ct);
        }

        await _accounts.AddAsync(input, ct);
        return input;
    }

    /// <inheritdoc />
    public async Task<Account?> UpdateAsync(Guid id, AccountUpdate update, CancellationToken ct = default)
    {
        var existing = await _accounts.GetByIdAsync(id, ct: ct);
        if (existing is null) return null;

        existing.Label = update.Label;
        existing.Description = update.Description;
        existing.Role = update.Role;
        existing.Enabled = update.Enabled;

        await _accounts.UpdateAsync(existing, ct);
        return existing;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        // Same reasoning as `SchemaService.DeleteAsync`: an account that owns live submissions
        // can't be soft-deleted without orphaning its samples in the projection. The safer
        // workflow is to disable the account so it can no longer authenticate while history
        // (and the audit trail) remains intact.
        var existing = await _accounts.GetByIdAsync(id, ct: ct);
        if (existing is null) return; // idempotent: nothing to delete

        if (await _samples.IsAccountInUseAsync(id, ct))
            throw new ConflictException(
                $"Account '{existing.Label ?? existing.Name}' has submitted data and cannot be deleted. " +
                "Disable it instead to revoke access while keeping the history intact.");

        await _accounts.SoftDeleteAsync(id, ct);
    }
}
