using System.Net.Http;
using System.Text;
using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Mongo;
using Ingest.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Webhooks;

/// <summary>
/// Drains the webhook outbox: picks up due pending deliveries, POSTs each to its endpoint with the
/// signature/idempotency headers, and records the outcome. A non-2xx response (or a transport
/// error) is retried with exponential backoff up to <see cref="WebhookWorkerOptions.MaxAttempts"/>;
/// an invalid or disallowed URL is failed permanently with a clear reason rather than retried.
/// </summary>
public sealed class WebhookDispatchService : IWebhookDispatchService
{
    /// <summary>Name of the typed <see cref="HttpClient"/> registered for webhook delivery.</summary>
    public const string HttpClientName = "webhooks";

    private static readonly TimeSpan BackoffBase = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BackoffCap = TimeSpan.FromHours(1);

    private readonly MongoContext _ctx;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ISecretProtector _protector;
    private readonly IAuditContext _audit;
    private readonly WebhookOptions _options;
    private readonly ILogger<WebhookDispatchService> _logger;

    /// <summary>Create a new <see cref="WebhookDispatchService"/>.</summary>
    public WebhookDispatchService(
        MongoContext ctx,
        IHttpClientFactory httpFactory,
        ISecretProtector protector,
        IAuditContext audit,
        IOptions<WebhookOptions> options,
        ILogger<WebhookDispatchService> logger)
    {
        _ctx = ctx;
        _httpFactory = httpFactory;
        _protector = protector;
        _audit = audit;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<WebhookDrainResult> DrainAsync(int max, CancellationToken ct = default)
    {
        var now = _audit.UtcNow;
        var fb = Builders<WebhookDelivery>.Filter;
        var due = fb.And(
            fb.Eq(d => d.Status, WebhookDeliveryStatus.Pending),
            fb.Or(fb.Eq(d => d.NextAttemptAt, null), fb.Lte(d => d.NextAttemptAt, now)));

        var pending = await _ctx.WebhookDeliveries
            .Find(due)
            .SortBy(d => d.CreatedAt)
            .Limit(Math.Clamp(max, 1, 500))
            .ToListAsync(ct);

        if (pending.Count == 0) return new WebhookDrainResult(0, 0);

        // Cache endpoints referenced this pass so we resolve each signing secret at most once.
        var endpointCache = new Dictionary<Guid, WebhookEndpoint?>();
        var client = _httpFactory.CreateClient(HttpClientName);
        int sent = 0, failed = 0;

        foreach (var delivery in pending)
        {
            ct.ThrowIfCancellationRequested();

            if (UrlRejectionReason(delivery.Url) is { } reason)
            {
                await MarkFailedAsync(delivery, reason, null, ct);
                failed++;
                continue;
            }

            if (!endpointCache.TryGetValue(delivery.EndpointId, out var endpoint))
            {
                endpoint = await _ctx.WebhookEndpoints
                    .Find(e => e.Id == delivery.EndpointId)
                    .FirstOrDefaultAsync(ct);
                endpointCache[delivery.EndpointId] = endpoint;
            }

            try
            {
                var status = await PostAsync(client, delivery, endpoint, ct);
                if (status is >= 200 and < 300)
                {
                    delivery.DeliveredAt = _audit.UtcNow;
                    delivery.LastStatusCode = status;
                    await MarkAsync(delivery, WebhookDeliveryStatus.Sent, null, ct);
                    sent++;
                }
                else
                {
                    await RecordFailureAsync(delivery, $"Endpoint responded {status}.", status, ct);
                    failed++;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Per-request timeout (linked token), not a host shutdown — treat as transient.
                await RecordFailureAsync(delivery, "Request timed out.", null, ct);
                failed++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                await RecordFailureAsync(delivery, ex.Message, null, ct);
                failed++;
                _logger.LogWarning(ex, "Webhook delivery {Id} failed (attempt {Attempt}/{Max}).",
                    delivery.Id, delivery.Attempts + 1, _options.Worker.MaxAttempts);
            }
        }

        return new WebhookDrainResult(sent, failed);
    }

    private async Task<int> PostAsync(HttpClient client, WebhookDelivery delivery, WebhookEndpoint? endpoint, CancellationToken ct)
    {
        using var content = new StringContent(delivery.PayloadJson, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, delivery.Url) { Content = content };

        var timestamp = ((DateTimeOffset)DateTime.SpecifyKind(_audit.UtcNow, DateTimeKind.Utc)).ToUnixTimeSeconds().ToString();
        request.Headers.TryAddWithoutValidation("X-Ingest-Event", delivery.Kind.ToWire());
        request.Headers.TryAddWithoutValidation("X-Ingest-Event-Id", delivery.EventId);
        request.Headers.TryAddWithoutValidation("X-Ingest-Delivery", delivery.Id.ToString());
        request.Headers.TryAddWithoutValidation("X-Ingest-Timestamp", timestamp);

        var secret = _protector.Unprotect(endpoint?.SecretCipher);
        if (!string.IsNullOrEmpty(secret))
            request.Headers.TryAddWithoutValidation("X-Ingest-Signature", WebhookSigner.Sign(secret, timestamp, delivery.PayloadJson));

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.RequestTimeoutSeconds)));

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        return (int)response.StatusCode;
    }

    /// <summary>Return a rejection reason for an unusable URL, or null when it's safe to attempt.</summary>
    private string? UrlRejectionReason(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return $"'{url}' is not a valid absolute http(s) URL.";

        var suffixes = _options.AllowedHostSuffixes;
        if (suffixes.Length > 0 &&
            !suffixes.Any(s => uri.Host.EndsWith(s.TrimStart('*'), StringComparison.OrdinalIgnoreCase)))
            return $"Host '{uri.Host}' is not in the configured webhook allow-list.";

        return null;
    }

    /// <summary>Record a transient failure: bump attempts and either schedule a backoff retry or fail permanently.</summary>
    private Task RecordFailureAsync(WebhookDelivery delivery, string error, int? statusCode, CancellationToken ct)
    {
        delivery.Attempts++;
        delivery.LastStatusCode = statusCode;
        if (delivery.Attempts >= _options.Worker.MaxAttempts)
            return MarkAsync(delivery, WebhookDeliveryStatus.Failed, error, ct);

        // Exponential backoff: base * 2^(attempts-1), capped.
        var delaySeconds = Math.Min(BackoffCap.TotalSeconds, BackoffBase.TotalSeconds * Math.Pow(2, delivery.Attempts - 1));
        delivery.NextAttemptAt = _audit.UtcNow.AddSeconds(delaySeconds);
        return MarkAsync(delivery, WebhookDeliveryStatus.Pending, error, ct);
    }

    private Task MarkFailedAsync(WebhookDelivery delivery, string error, int? statusCode, CancellationToken ct)
    {
        delivery.Attempts++;
        delivery.LastStatusCode = statusCode;
        return MarkAsync(delivery, WebhookDeliveryStatus.Failed, error, ct);
    }

    private Task MarkAsync(WebhookDelivery delivery, WebhookDeliveryStatus status, string? error, CancellationToken ct)
    {
        delivery.Status = status;
        delivery.LastError = error;
        delivery.ModifiedAt = _audit.UtcNow;
        delivery.ModifiedBy = _audit.UserName;
        return _ctx.WebhookDeliveries.ReplaceOneAsync(
            Builders<WebhookDelivery>.Filter.Eq(d => d.Id, delivery.Id), delivery, cancellationToken: ct);
    }
}
