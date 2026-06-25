using Ingest.Core.Common;
using Ingest.Core.Entities;

namespace Ingest.Core.Abstractions;

/// <summary>
/// Pairs a newly-rotated <see cref="ApiKey"/> entity with the plaintext it grants the caller —
/// the only time the plaintext ever leaves the server, since the registry stores only its hash.
/// Distinct from the lower-level <see cref="GeneratedApiKey"/> in <see cref="IApiKeyHasher"/>
/// (which exposes the salt+hash for persistence).
/// </summary>
/// <param name="Entity">The persisted key entity (metadata only — id, prefix, timestamps, …).</param>
/// <param name="Plaintext">The secret the caller must record now; it cannot be recovered later.</param>
public sealed record RotatedApiKey(ApiKey Entity, string Plaintext);

/// <summary>
/// Owns the lifecycle of API keys: validates the parent account, delegates the cryptographic work
/// to <see cref="IApiKeyHasher"/>, persists the entity, and makes revocation idempotent so a
/// retry never returns 404.
/// </summary>
public interface IApiKeyService
{
    /// <summary>List every key attached to an account (metadata only — plaintext is never returned).</summary>
    /// <param name="accountId">Account to inspect.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The keys ordered as the underlying repository returns them.</returns>
    /// <exception cref="NotFoundException">No account with that id.</exception>
    Task<IReadOnlyList<ApiKey>> ListAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>Generate and persist a new key for the account, returning it together with its one-time plaintext.</summary>
    /// <param name="accountId">Account that will own the new key.</param>
    /// <param name="expiresAt">Optional absolute expiry. When supplied it must be in the future and no more than two years out; <c>null</c> means the key never expires.</param>
    /// <param name="description">Optional free-form note describing who/what the key is for. Trimmed; blank is stored as none.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The new key paired with its plaintext.</returns>
    /// <exception cref="NotFoundException">No account with that id.</exception>
    /// <exception cref="ValidationException">The supplied <paramref name="expiresAt"/> is in the past or more than two years in the future, or <paramref name="description"/> is too long.</exception>
    Task<RotatedApiKey> RotateAsync(Guid accountId, DateTime? expiresAt = null, string? description = null, CancellationToken ct = default);

    /// <summary>Update a key's free-form description (the only mutable, non-lifecycle field).</summary>
    /// <param name="accountId">Account that owns the key.</param>
    /// <param name="keyId">Key to annotate.</param>
    /// <param name="description">New description; trimmed, with blank stored as none.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated key, or <c>null</c> if no such key exists for the account.</returns>
    /// <exception cref="ValidationException">The supplied <paramref name="description"/> is too long.</exception>
    Task<ApiKey?> UpdateDescriptionAsync(Guid accountId, Guid keyId, string? description, CancellationToken ct = default);

    /// <summary>Mark a key revoked.</summary>
    /// <remarks>Calling revoke twice is safe; the returned entity simply carries the existing revocation timestamp.</remarks>
    /// <param name="accountId">Account that owns the key.</param>
    /// <param name="keyId">Key to revoke.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The revoked key, or <c>null</c> if no such key exists for the account.</returns>
    Task<ApiKey?> RevokeAsync(Guid accountId, Guid keyId, CancellationToken ct = default);
}
