using System.Security.Cryptography;
using System.Text;
using Ingest.Core.Abstractions;
using Microsoft.Extensions.Options;

namespace Ingest.Infrastructure.Security;

/// <summary>
/// Default <see cref="IApiKeyHasher"/>. Generates keys of the form <c>{keyId}.{secret}</c> where
/// <c>keyId</c> is 8 random bytes (hex) and <c>secret</c> is 32 random bytes (base64url).
/// Storage carries a per-key 16-byte salt; verification re-computes <c>HMAC-SHA256(pepper,
/// salt || secret)</c> and compares in constant time. The pepper comes from configuration and
/// must be the same on every instance behind a load balancer.
/// </summary>
public sealed class ApiKeyHasher : IApiKeyHasher
{
    private readonly ApiKeyOptions _options;

    /// <summary>Create a new <see cref="ApiKeyHasher"/>.</summary>
    /// <param name="options">Bound <see cref="ApiKeyOptions"/>; provides the pepper.</param>
    public ApiKeyHasher(IOptions<ApiKeyOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public GeneratedApiKey Generate()
    {
        var keyId = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var secretBytes = RandomNumberGenerator.GetBytes(32);
        var secret = Base64Url(secretBytes);
        var saltBytes = RandomNumberGenerator.GetBytes(16);
        var salt = Convert.ToHexString(saltBytes).ToLowerInvariant();
        var hash = Hash(secret, salt);
        var plaintext = $"{keyId}.{secret}";
        return new GeneratedApiKey(plaintext, keyId, secret, salt, hash);
    }

    /// <inheritdoc />
    public GeneratedApiKey? Import(string plaintext)
    {
        if (!TrySplit(plaintext, out var keyId, out var secret)) return null;
        var saltBytes = RandomNumberGenerator.GetBytes(16);
        var salt = Convert.ToHexString(saltBytes).ToLowerInvariant();
        var hash = Hash(secret, salt);
        return new GeneratedApiKey(plaintext, keyId, secret, salt, hash);
    }

    /// <inheritdoc />
    public bool TrySplit(string presented, out string keyId, out string secret)
    {
        keyId = string.Empty;
        secret = string.Empty;
        if (string.IsNullOrWhiteSpace(presented)) return false;
        var idx = presented.IndexOf('.');
        if (idx <= 0 || idx == presented.Length - 1) return false;
        keyId = presented[..idx];
        secret = presented[(idx + 1)..];
        return true;
    }

    /// <inheritdoc />
    public bool Verify(string secret, string storedSalt, string storedHash)
    {
        var computed = Hash(secret, storedSalt);
        var a = Encoding.ASCII.GetBytes(computed);
        var b = Encoding.ASCII.GetBytes(storedHash);
        if (a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <inheritdoc />
    public string Hash(string secret, string salt)
    {
        var pepper = Encoding.UTF8.GetBytes(_options.Pepper);
        var saltBytes = Convert.FromHexString(salt);
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var input = new byte[saltBytes.Length + secretBytes.Length];
        Buffer.BlockCopy(saltBytes, 0, input, 0, saltBytes.Length);
        Buffer.BlockCopy(secretBytes, 0, input, saltBytes.Length, secretBytes.Length);
        using var mac = new HMACSHA256(pepper);
        var digest = mac.ComputeHash(input);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
