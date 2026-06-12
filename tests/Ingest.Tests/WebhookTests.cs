using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Security;
using Ingest.Infrastructure.Webhooks;
using Microsoft.Extensions.Options;

namespace Ingest.Tests;

/// <summary>
/// Tests for <see cref="WebhookSigner"/> — the HMAC-SHA256 signature put in the
/// <c>X-Ingest-Signature</c> header. The signed string is <c>{timestamp}.{body}</c>.
/// </summary>
public class WebhookSignerTests
{
    private const string Secret = "whsec_super-secret-value";
    private const string Timestamp = "1750000000";
    private const string Body = "{\"event\":\"submission.accepted\",\"data\":{}}";

    [Fact]
    public void Signature_matches_an_independent_hmac_over_timestamp_dot_body()
    {
        var expected = "sha256=" + Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(Timestamp + "." + Body)));

        Assert.Equal(expected, WebhookSigner.Sign(Secret, Timestamp, Body));
    }

    [Fact]
    public void Signature_is_prefixed_and_lowercase_hex()
    {
        var sig = WebhookSigner.Sign(Secret, Timestamp, Body);
        Assert.StartsWith("sha256=", sig);
        var hex = sig["sha256=".Length..];
        Assert.Equal(64, hex.Length); // SHA-256 → 32 bytes → 64 hex chars
        Assert.Equal(hex.ToLowerInvariant(), hex);
    }

    [Fact]
    public void Signature_is_deterministic_for_the_same_inputs()
    {
        Assert.Equal(WebhookSigner.Sign(Secret, Timestamp, Body), WebhookSigner.Sign(Secret, Timestamp, Body));
    }

    [Fact]
    public void Signature_changes_when_any_input_changes()
    {
        var baseSig = WebhookSigner.Sign(Secret, Timestamp, Body);
        Assert.NotEqual(baseSig, WebhookSigner.Sign("other-secret", Timestamp, Body));
        Assert.NotEqual(baseSig, WebhookSigner.Sign(Secret, "1750000001", Body));
        Assert.NotEqual(baseSig, WebhookSigner.Sign(Secret, Timestamp, Body + " "));
    }

    [Fact]
    public void Timestamp_is_bound_into_the_signature_so_a_swapped_timestamp_is_detected()
    {
        // Same concatenated bytes split differently must not collide: "1.23" vs "12.3".
        Assert.NotEqual(WebhookSigner.Sign(Secret, "1", "23"), WebhookSigner.Sign(Secret, "12", "3"));
    }
}

/// <summary>
/// Tests for <see cref="WebhookSecretProtector"/> — the AES-GCM wrapper that protects webhook
/// signing secrets at rest, keyed off the API-key pepper with a webhook-specific domain separator.
/// </summary>
public class WebhookSecretProtectorTests
{
    private static WebhookSecretProtector New(string pepper = "a-long-dev-pepper-value") =>
        new(Options.Create(new ApiKeyOptions { Pepper = pepper }));

    [Fact]
    public void Round_trips_a_secret()
    {
        var p = New();
        var token = p.Protect("whsec_abc123");
        Assert.NotNull(token);
        Assert.NotEqual("whsec_abc123", token);
        Assert.Equal("whsec_abc123", p.Unprotect(token));
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
    public void The_email_protector_cannot_read_a_webhook_token_even_with_the_same_pepper()
    {
        // Domain separation: the webhook protector salts the key differently from the email one,
        // so a token from one must not decrypt with the other despite sharing the pepper.
        const string pepper = "shared-pepper";
        var webhookToken = New(pepper).Protect("secret");
        var email = new Ingest.Infrastructure.Email.EmailSecretProtector(Options.Create(new ApiKeyOptions { Pepper = pepper }));
        Assert.Throws<AuthenticationTagMismatchException>(() => email.Unprotect(webhookToken));
    }

    [Fact]
    public void Tampered_token_is_rejected()
    {
        var p = New();
        var token = p.Protect("secret")!;
        var bytes = Convert.FromBase64String(token);
        bytes[^1] ^= 0xFF;
        var tampered = Convert.ToBase64String(bytes);
        Assert.ThrowsAny<CryptographicException>(() => p.Unprotect(tampered));
    }
}

/// <summary>Tests for the wire-name mapping and payload serialiser conventions.</summary>
public class WebhookEventNameTests
{
    [Theory]
    [InlineData(WebhookEventKind.SubmissionAccepted, "submission.accepted")]
    [InlineData(WebhookEventKind.SubmissionWarnings, "submission.warnings")]
    [InlineData(WebhookEventKind.WindowUpcoming, "window.upcoming")]
    [InlineData(WebhookEventKind.WindowMissed, "window.missed")]
    public void ToWire_maps_each_kind_to_its_dotted_name(WebhookEventKind kind, string expected)
    {
        Assert.Equal(expected, kind.ToWire());
    }

    [Fact]
    public void Payload_serialiser_uses_camelCase_and_omits_nulls()
    {
        var json = JsonSerializer.Serialize(new { fooBar = 1, skipped = (string?)null }, WebhookJson.Options);
        Assert.Contains("\"fooBar\":1", json);
        Assert.DoesNotContain("skipped", json);
    }
}
