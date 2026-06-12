using System.Security.Cryptography;
using Ingest.Infrastructure.Email;
using Ingest.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Ingest.Tests;

/// <summary>
/// Tests for <see cref="EmailSecretProtector"/> — the AES-GCM wrapper that protects the SMTP
/// password at rest using the API-key pepper as key material.
/// </summary>
public class EmailSecretProtectorTests
{
    private static EmailSecretProtector New(string pepper = "a-long-dev-pepper-value") =>
        new(Options.Create(new ApiKeyOptions { Pepper = pepper }));

    [Fact]
    public void Round_trips_a_secret()
    {
        var p = New();
        var token = p.Protect("hunter2");
        Assert.NotNull(token);
        Assert.NotEqual("hunter2", token);
        Assert.Equal("hunter2", p.Unprotect(token));
    }

    [Fact]
    public void Null_or_empty_passes_through_as_null()
    {
        var p = New();
        Assert.Null(p.Protect(null));
        Assert.Null(p.Protect(""));
        Assert.Null(p.Unprotect(null));
        Assert.Null(p.Unprotect(""));
    }

    [Fact]
    public void Two_encryptions_of_the_same_value_differ_but_both_decrypt()
    {
        var p = New();
        var a = p.Protect("same");
        var b = p.Protect("same");
        Assert.NotEqual(a, b); // random nonce per call
        Assert.Equal("same", p.Unprotect(a));
        Assert.Equal("same", p.Unprotect(b));
    }

    [Fact]
    public void A_token_cannot_be_decrypted_with_a_different_pepper()
    {
        var token = New("pepper-one").Protect("secret");
        var other = New("pepper-two");
        Assert.Throws<AuthenticationTagMismatchException>(() => other.Unprotect(token));
    }

    [Fact]
    public void Tampered_token_is_rejected()
    {
        var p = New();
        var token = p.Protect("secret")!;
        // Flip a character in the base64 payload.
        var bytes = Convert.FromBase64String(token);
        bytes[^1] ^= 0xFF;
        var tampered = Convert.ToBase64String(bytes);
        Assert.ThrowsAny<CryptographicException>(() => p.Unprotect(tampered));
    }
}
