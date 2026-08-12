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
    private readonly IAuditLogService _auditLog;

    /// <summary>Create a new <see cref="ApiKeyService"/>.</summary>
    /// <param name="accounts">Account repository for the parent-existence check.</param>
    /// <param name="keys">API-key repository.</param>
    /// <param name="hasher">Hasher used to generate fresh keys.</param>
    /// <param name="audit">Audit context for the revocation timestamp.</param>
    /// <param name="auditLog">Audit log used to record key creation/revocation.</param>
    public ApiKeyService(
        IAccountRepository accounts,
        IApiKeyRepository keys,
        IApiKeyHasher hasher,
        IAuditContext audit,
        IAuditLogService auditLog)
    {
        _accounts = accounts;
        _keys = keys;
        _hasher = hasher;
        _audit = audit;
        _auditLog = auditLog;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ApiKey>> ListAsync(Guid accountId, CancellationToken ct = default) =>
        _keys.ListByAccountAsync(accountId, ct);

    /// <summary>Largest lifetime an API key may be given at creation time.</summary>
    public const int MaxLifetimeYears = 2;

    /// <summary>Longest a key's free-form description may be.</summary>
    public const int MaxDescriptionLength = 200;

    /// <inheritdoc />
    public async Task<RotatedApiKey> RotateAsync(Guid accountId, DateTime? expiresAt = null, string? description = null, CancellationToken ct = default)
    {
        var account = await _accounts.GetByIdAsync(accountId, ct: ct)
            ?? throw new NotFoundException($"Account '{accountId}'");

        var normalizedExpiry = NormalizeAndValidateExpiry(expiresAt);

        var generated = _hasher.Generate();
        var entity = new ApiKey
        {
            AccountId = account.Id,
            KeyId = generated.KeyId,
            Hash = generated.Hash,
            Salt = generated.Salt,
            ExpiresAt = normalizedExpiry,
            Description = NormalizeAndValidateDescription(description),
        };
        await _keys.AddAsync(entity, ct);
        await _auditLog.RecordAsync(AuditTargetType.ApiKey, AuditChangeType.Create, entity.Id, entity.KeyId, ct);
        return new RotatedApiKey(entity, generated.Plaintext);
    }

    /// <inheritdoc />
    public async Task<ApiKey?> UpdateDescriptionAsync(Guid accountId, Guid keyId, string? description, CancellationToken ct = default)
    {
        // Same (account, key) pairing as revoke so a stray keyId from another account can't be touched.
        var all = await _keys.ListByAccountAsync(accountId, ct);
        var key = all.FirstOrDefault(k => k.Id == keyId);
        if (key is null) return null;

        var normalized = NormalizeAndValidateDescription(description);
        if (key.Description != normalized)
        {
            key.Description = normalized;
            await _keys.UpdateAsync(key, ct);
            await _auditLog.RecordAsync(AuditTargetType.ApiKey, AuditChangeType.Edit, key.Id, key.KeyId, ct);
        }
        return key;
    }

    /// <summary>Trim a caller-supplied description, store blank as <c>null</c>, and cap its length.</summary>
    private static string? NormalizeAndValidateDescription(string? description)
    {
        var trimmed = description?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;

        if (trimmed.Length > MaxDescriptionLength)
            throw new ValidationException(new[]
            {
                Diagnostic.Create(
                    DiagnosticCodes.Accounts.ApiKeyDescriptionTooLong,
                    $"API key description cannot be longer than {MaxDescriptionLength} characters.",
                    ("maxLength", MaxDescriptionLength),
                    ("actualLength", trimmed.Length)),
            });

        return trimmed;
    }

    /// <summary>
    /// Coerce a caller-supplied expiry to UTC and enforce the lifecycle rules: it must be in the
    /// future and no more than <see cref="MaxLifetimeYears"/> years out. A <c>null</c> expiry (the
    /// key never expires) is returned untouched.
    /// </summary>
    private DateTime? NormalizeAndValidateExpiry(DateTime? expiresAt)
    {
        if (expiresAt is not { } value) return null;

        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

        var now = _audit.UtcNow;
        if (utc <= now)
            throw new ValidationException(new[]
            {
                Diagnostic.Create(
                    DiagnosticCodes.Accounts.ApiKeyExpiryNotFuture,
                    "API key expiry must be in the future.",
                    ("expiry", utc),
                    ("now", now)),
            });

        if (utc > now.AddYears(MaxLifetimeYears))
            throw new ValidationException(new[]
            {
                Diagnostic.Create(
                    DiagnosticCodes.Accounts.ApiKeyExpiryTooDistant,
                    $"API key expiry cannot be more than {MaxLifetimeYears} years in the future.",
                    ("expiry", utc),
                    ("now", now),
                    ("maxLifetimeYears", MaxLifetimeYears)),
            });

        return utc;
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
            // A revoke is the soft lifecycle equivalent of a delete (the row is kept), so it is
            // logged as a Delete of the key.
            await _auditLog.RecordAsync(AuditTargetType.ApiKey, AuditChangeType.Delete, key.Id, key.KeyId, ct);
        }
        return key;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid accountId, Guid keyId, CancellationToken ct = default)
    {
        // Same (account, key) pairing as revoke so a stray keyId from another account can't be deleted.
        var all = await _keys.ListByAccountAsync(accountId, ct);
        var key = all.FirstOrDefault(k => k.Id == keyId);
        if (key is null) return false;

        var deleted = await _keys.DeleteAsync(key.Id, ct);
        if (deleted)
            await _auditLog.RecordAsync(AuditTargetType.ApiKey, AuditChangeType.Delete, key.Id, key.KeyId, ct);
        return deleted;
    }
}
