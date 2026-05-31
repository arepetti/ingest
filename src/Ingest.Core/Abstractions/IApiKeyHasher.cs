namespace Ingest.Core.Abstractions;

/// <summary>
/// Low-level cryptographic surface for API keys. The registry never stores or transports
/// plaintext keys after generation — instead it persists the salt and HMAC-SHA256 hash so a
/// presented key can be verified in constant time without ever reversing the digest.
/// </summary>
public interface IApiKeyHasher
{
    /// <summary>Generate a fresh key, returning both the plaintext to hand back to the caller and the parts that need to be persisted.</summary>
    /// <returns>A <see cref="GeneratedApiKey"/> whose <c>Plaintext</c> field is the only value the user will ever see.</returns>
    GeneratedApiKey Generate();

    /// <summary>
    /// Derive the persisted artefacts (id, salt, hash) for a caller-supplied plaintext key — used
    /// to seed a known key from configuration (e.g. the bootstrap admin key) rather than minting a
    /// random one. A fresh random salt is generated, exactly as in <see cref="Generate"/>.
    /// </summary>
    /// <param name="plaintext">A well-formed <c>{keyId}.{secret}</c> string.</param>
    /// <returns>The components to persist, or <c>null</c> when <paramref name="plaintext"/> is malformed.</returns>
    GeneratedApiKey? Import(string plaintext);

    /// <summary>Split a presented key string into its id and secret components.</summary>
    /// <remarks>
    /// Intentionally not named <c>TryParse</c>: ASP.NET Core's Minimal API parameter binding
    /// scans candidate types for a static <c>TryParse</c> method and throws if it finds one
    /// with the wrong shape, which would prevent any endpoint binding this type from running.
    /// </remarks>
    /// <param name="presented">The "{keyId}.{secret}" string supplied by the client.</param>
    /// <param name="keyId">The leading id portion on success.</param>
    /// <param name="secret">The trailing secret portion on success.</param>
    /// <returns>True if the string is well-formed; otherwise false (and the out parameters are undefined).</returns>
    bool TrySplit(string presented, out string keyId, out string secret);

    /// <summary>Constant-time comparison of a presented secret against the stored salt+hash.</summary>
    /// <param name="secret">The plaintext secret submitted by the client.</param>
    /// <param name="storedSalt">Salt loaded from the database.</param>
    /// <param name="storedHash">Hash loaded from the database.</param>
    /// <returns>True iff the secret reproduces the stored hash.</returns>
    bool Verify(string secret, string storedSalt, string storedHash);

    /// <summary>Compute the canonical hash of a secret under a given salt.</summary>
    /// <param name="secret">Plaintext secret.</param>
    /// <param name="salt">Salt to combine with the secret.</param>
    /// <returns>The Base64-encoded hash.</returns>
    string Hash(string secret, string salt);
}

/// <summary>
/// Result of <see cref="IApiKeyHasher.Generate"/>: the plaintext that must be handed once to the
/// caller plus the persisted artefacts (id, salt, hash) the registry stores.
/// </summary>
/// <param name="Plaintext">The full "{KeyId}.{Secret}" string to display to the user exactly once.</param>
/// <param name="KeyId">Public id portion (also used to prefix indexes for fast lookup).</param>
/// <param name="Secret">The secret portion in cleartext — never persisted, never logged.</param>
/// <param name="Salt">Salt persisted alongside the hash.</param>
/// <param name="Hash">HMAC-SHA256 of the secret under the salt; what the registry stores.</param>
public sealed record GeneratedApiKey(string Plaintext, string KeyId, string Secret, string Salt, string Hash);
