using System.Security.Cryptography;
using System.Text;
using Ingest.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Ingest.Infrastructure.Email;

/// <summary>
/// Symmetric protector for the one secret we persist in clear-text-adjacent storage: the SMTP
/// password. We reuse the server-wide <see cref="ApiKeyOptions.Pepper"/> as the key material so
/// there's no extra secret to configure — the pepper is already a required production secret. The
/// password is therefore only as protected as the pepper, which is the intended threat model
/// (defence against casual database reads, not against an attacker who already holds app secrets).
/// </summary>
public interface IEmailSecretProtector
{
    /// <summary>Encrypt a plaintext secret into an opaque, self-describing token. Null/empty → null.</summary>
    string? Protect(string? plaintext);

    /// <summary>Decrypt a token produced by <see cref="Protect"/>. Null/empty → null; tampered/garbage → throws.</summary>
    string? Unprotect(string? token);
}

/// <summary>AES-GCM implementation of <see cref="IEmailSecretProtector"/>.</summary>
public sealed class EmailSecretProtector : IEmailSecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    /// <summary>Create a protector keyed off the API-key pepper.</summary>
    /// <param name="apiKey">Bound API-key options (only the pepper is read).</param>
    public EmailSecretProtector(IOptions<ApiKeyOptions> apiKey)
    {
        // Derive a fixed 256-bit key from the pepper. SHA-256 gives us exactly 32 bytes.
        _key = SHA256.HashData(Encoding.UTF8.GetBytes("ingest-email:" + apiKey.Value.Pepper));
    }

    /// <inheritdoc />
    public string? Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return null;

        var plain = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        // Layout: nonce | tag | ciphertext, base64-encoded.
        var blob = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, blob, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, blob, NonceSize + TagSize, cipher.Length);
        return Convert.ToBase64String(blob);
    }

    /// <inheritdoc />
    public string? Unprotect(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;

        var blob = Convert.FromBase64String(token);
        if (blob.Length < NonceSize + TagSize)
            throw new CryptographicException("Email secret token is too short to be valid.");

        var nonce = blob.AsSpan(0, NonceSize);
        var tag = blob.AsSpan(NonceSize, TagSize);
        var cipher = blob.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}
