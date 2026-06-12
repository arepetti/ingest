using System.Text.Json;
using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Mongo;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Webhooks;

/// <summary>
/// MongoDB-backed fan-out. Finds the enabled endpoints subscribed to an event, renders the JSON
/// envelope once, and inserts one <see cref="WebhookDelivery"/> per endpoint. Enqueue is
/// deduplicated through the unique <c>(eventId, endpointId)</c> index, exactly like the
/// notification log — calling it twice for the same event is a no-op for already-enqueued pairs.
/// </summary>
public sealed class WebhookPublisher : IWebhookPublisher
{
    private readonly MongoContext _ctx;
    private readonly IAuditContext _audit;
    private readonly ILogger<WebhookPublisher> _logger;

    /// <summary>Create a new <see cref="WebhookPublisher"/>.</summary>
    public WebhookPublisher(MongoContext ctx, IAuditContext audit, ILogger<WebhookPublisher> logger)
    {
        _ctx = ctx;
        _audit = audit;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> PublishAsync(WebhookEventKind kind, string eventId, object data, Guid? serviceAccountId, CancellationToken ct = default)
    {
        var fb = Builders<WebhookEndpoint>.Filter;
        var filter = fb.And(
            fb.Eq(e => e.IsDeleted, false),
            fb.Eq(e => e.Enabled, true),
            fb.AnyEq(e => e.Events, kind));

        var endpoints = await _ctx.WebhookEndpoints.Find(filter).ToListAsync(ct);
        if (endpoints.Count == 0) return 0;

        var now = _audit.UtcNow;
        var envelope = new
        {
            @event = kind.ToWire(),
            eventId,
            occurredAt = now,
            data,
        };
        var payloadJson = JsonSerializer.Serialize(envelope, WebhookJson.Options);

        var enqueued = 0;
        foreach (var ep in endpoints)
        {
            // Honour the optional per-endpoint service filter.
            if (ep.ServiceAccountId is { } sid && sid != serviceAccountId) continue;

            var delivery = new WebhookDelivery
            {
                EndpointId = ep.Id,
                Url = ep.Url,
                Kind = kind,
                EventId = eventId,
                PayloadJson = payloadJson,
                Status = WebhookDeliveryStatus.Pending,
                RelatedAccountId = serviceAccountId,
                CreatedAt = now,
                ModifiedAt = now,
                CreatedBy = _audit.UserName,
                ModifiedBy = _audit.UserName,
            };

            try
            {
                await _ctx.WebhookDeliveries.InsertOneAsync(delivery, cancellationToken: ct);
                enqueued++;
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // Already enqueued for this (event, endpoint) — deduped.
            }
        }

        if (enqueued > 0)
            _logger.LogDebug("Webhook {Event} {EventId} enqueued to {Count} endpoint(s).", kind.ToWire(), eventId, enqueued);

        return enqueued;
    }
}
