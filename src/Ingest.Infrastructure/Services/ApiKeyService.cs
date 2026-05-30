using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;

namespace Ingest.Infrastructure.Services;

/// <summary>
/// Default implementation of <see cref="IApiKeyService"/>. Validates the parent account exists,
/// delegates hashing to <see cref="IApiKeyHasher"/>, and makes revocation idempotent so retries
/// never produce 404s.
/// </summary>
public sealed class ApiKeyService : IApiKeyService
{
    private readonly IAccountRepository _accounts;
    private readonly IApiKeyRepository _keys;
    private readonly IApiKeyHasher _hasher;
    private readonly IAuditContext _audit;

    /// <summary>Create a new <see cref="ApiKeyService"/>.</summary>
    /// <param name="accounts">Account repository for the parent-existence check.</param>
    /// <param name="keys">API-key repository.</param>
    /// <param name="hasher">Hasher used to generate fresh keys.</param>
    /// <param name="audit">Audit context for the revocation timestamp.</param>
    public ApiKeyService(
        IAccountRepository accounts,
        IApiKeyRepository keys,
        IApiKeyHasher hasher,
        IAuditContext audit)
    {
        _accounts = accounts;
        _keys = keys;
        _hasher = hasher;
        _audit = audit;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ApiKey>> ListAsync(Guid accountId, CancellationToken ct = default) =>
        _keys.ListByAccountAsync(accountId, ct);

    /// <inheritdoc />
    public async Task<RotatedApiKey> RotateAsync(Guid accountId, CancellationToken ct = default)
    {
        var account = await _accounts.GetByIdAsync(accountId, ct: ct)
            ?? throw new NotFoundException($"Account '{accountId}'");

        var generated = _hasher.Generate();
        var entity = new ApiKey
        {
            AccountId = account.Id,
            KeyId = generated.KeyId,
            Hash = generated.Hash,
            Salt = generated.Salt,
        };
        await _keys.AddAsync(entity, ct);
        return new RotatedApiKey(entity, generated.Plaintext);
    }

    /// <inheritdoc />
    public async Task<ApiKey?> RevokeAsync(Guid accountId, Guid keyId, CancellationToken ct = default)
    {
        // The list-then-filter (vs a direct GetByKeyId) is intentional: it ties revocation to the
        // (account, key) pair so a stray keyId from another account can't be revoked here.
        var all = await _keys.ListByAccountAsync(accountId, ct);
        var key = all.FirstOrDefault(k => k.Id == keyId);
        if (key is null) return null;

        // Idempotent: revoking an already-revoked key returns its current state without re-stamping.
        if (key.RevokedAt is null)
        {
            key.RevokedAt = _audit.UtcNow;
            await _keys.UpdateAsync(key, ct);
        }
        return key;
    }
}
