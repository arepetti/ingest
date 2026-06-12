using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Ingest.Infrastructure.Security;

/// <summary>
/// Symmetric protector for secrets we persist near clear text (webhook signing secrets, …). Like
/// the email password protector it reuses the server-wide <see cref="ApiKeyOptions.Pepper"/> as
/// key material so there is no extra secret to configure. The threat model is defence against
/// casual database reads, not against an attacker who already holds the app secrets.
/// </summary>
public interface ISecretProtector
{
    /// <summary>Encrypt a plaintext secret into an opaque, self-describing token. Null/empty → null.</summary>
    string? Protect(string? plaintext);

    /// <summary>Decrypt a token produced by <see cref="Protect"/>. Null/empty → null; tampered/garbage → throws.</summary>
    string? Unprotect(string? token);
}

/// <summary>AES-GCM implementation of <see cref="ISecretProtector"/> for the webhook subsystem.</summary>
public sealed class WebhookSecretProtector : ISecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    /// <summary>Create a protector keyed off the API-key pepper (with a webhook-specific domain separator).</summary>
    /// <param name="apiKey">Bound API-key options (only the pepper is read).</param>
    public WebhookSecretProtector(IOptions<ApiKeyOptions> apiKey)
    {
        _key = SHA256.HashData(Encoding.UTF8.GetBytes("ingest-webhook:" + apiKey.Value.Pepper));
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
            throw new CryptographicException("Webhook secret token is too short to be valid.");

        var nonce = blob.AsSpan(0, NonceSize);
        var tag = blob.AsSpan(NonceSize, TagSize);
        var cipher = blob.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}
