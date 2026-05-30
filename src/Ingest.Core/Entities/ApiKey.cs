using Ingest.Core.Common;

namespace Ingest.Core.Entities;

/// <summary>
/// Persisted state of an API key. The plaintext never reaches this entity — only its public
/// id portion, plus a salt and HMAC-SHA256 hash so a presented key can be verified in constant
/// time without ever reversing the digest.
/// </summary>
public sealed class ApiKey : AuditedEntity
{
    /// <summary>Owning account.</summary>
    public required Guid AccountId { get; set; }

    /// <summary>
    /// Public, non-secret identifier embedded in the plaintext as <c>{KeyId}.{secret}</c> for
    /// fast lookup. Indexed and unique.
    /// </summary>
    public required string KeyId { get; set; }

    /// <summary>Hex-encoded <c>HMAC-SHA256(pepper, salt || secret)</c>.</summary>
    public required string Hash { get; set; }

    /// <summary>Hex-encoded per-key random salt mixed into the hash.</summary>
    public required string Salt { get; set; }

    /// <summary>Optional absolute expiry. When non-null, the key stops authenticating once this timestamp is past.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Set when the key has been revoked. Idempotent — revoking a revoked key leaves this value unchanged.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>Whether the key currently authenticates requests.</summary>
    /// <param name="nowUtc">Reference time used to evaluate <see cref="ExpiresAt"/>.</param>
    /// <returns>True when the key is not deleted, not revoked, and either has no expiry or expires after <paramref name="nowUtc"/>.</returns>
    public bool IsActive(DateTime nowUtc) =>
        !IsDeleted && RevokedAt is null && (ExpiresAt is null || ExpiresAt > nowUtc);
}
