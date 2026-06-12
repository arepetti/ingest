using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ingest.Infrastructure.Webhooks;

/// <summary>Shared JSON options for webhook payloads: camelCase, enums as strings, nulls omitted.</summary>
public static class WebhookJson
{
    /// <summary>The serializer options every webhook payload is built with.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>
/// Computes the HMAC-SHA256 signature sent in the <c>X-Ingest-Signature</c> header. The signed
/// string is <c>{timestamp}.{body}</c> so a consumer can reject replays by also checking the
/// <c>X-Ingest-Timestamp</c> header is recent. Kept as a pure static for easy unit testing.
/// </summary>
public static class WebhookSigner
{
    /// <summary>Return the header value <c>sha256=&lt;lowercase-hex&gt;</c> for the given secret, timestamp and body.</summary>
    /// <param name="secret">The endpoint's plaintext signing secret.</param>
    /// <param name="timestamp">Unix-seconds timestamp string also sent in <c>X-Ingest-Timestamp</c>.</param>
    /// <param name="body">The exact request body bytes, as a UTF-8 string.</param>
    public static string Sign(string secret, string timestamp, string body)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var data = Encoding.UTF8.GetBytes(timestamp + "." + body);
        var hash = HMACSHA256.HashData(key, data);
        return "sha256=" + Convert.ToHexStringLower(hash);
    }
}
