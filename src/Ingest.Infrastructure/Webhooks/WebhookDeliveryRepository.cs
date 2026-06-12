using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Webhooks;

/// <summary>MongoDB-backed access to the webhook delivery outbox: the admin log, redelivery, and the GDPR/retention purge paths.</summary>
public sealed class WebhookDeliveryRepository : IWebhookDeliveryRepository
{
    private readonly MongoContext _ctx;
    private readonly IAuditContext _audit;

    /// <summary>Create a new <see cref="WebhookDeliveryRepository"/>.</summary>
    public WebhookDeliveryRepository(MongoContext ctx, IAuditContext audit)
    {
        _ctx = ctx;
        _audit = audit;
    }

    /// <inheritdoc />
    public async Task<PagedResult<WebhookDelivery>> ListAsync(PageRequest request, WebhookDeliveryStatus? status = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var fb = Builders<WebhookDelivery>.Filter;
        var filter = status is null ? fb.Empty : fb.Eq(d => d.Status, status.Value);
        // from inclusive, to exclusive (mirrors the email outbox); coerce to UTC so the comparison
        // matches how CreatedAt is stored.
        if (from is { } f)
            filter &= fb.Gte(d => d.CreatedAt, DateTime.SpecifyKind(f, DateTimeKind.Utc));
        if (to is { } u)
            filter &= fb.Lt(d => d.CreatedAt, DateTime.SpecifyKind(u, DateTimeKind.Utc));

        var total = await _ctx.WebhookDeliveries.CountDocumentsAsync(filter, cancellationToken: ct);
        var items = await _ctx.WebhookDeliveries
            .Find(filter)
            .SortByDescending(d => d.CreatedAt)
            .Skip(request.Skip)
            .Limit(request.Take)
            .ToListAsync(ct);

        return new PagedResult<WebhookDelivery>(items, total, request.Page, request.Take);
    }

    /// <inheritdoc />
    public async Task<bool> RequeueAsync(Guid deliveryId, CancellationToken ct = default)
    {
        var now = _audit.UtcNow;
        var update = Builders<WebhookDelivery>.Update
            .Set(d => d.Status, WebhookDeliveryStatus.Pending)
            .Set(d => d.NextAttemptAt, (DateTime?)null)
            .Set(d => d.Attempts, 0)
            .Set(d => d.LastError, (string?)null)
            .Set(d => d.ModifiedAt, now)
            .Set(d => d.ModifiedBy, _audit.UserName);

        var result = await _ctx.WebhookDeliveries.UpdateOneAsync(d => d.Id == deliveryId, update, cancellationToken: ct);
        return result.MatchedCount > 0;
    }

    /// <inheritdoc />
    public async Task<long> HardDeleteForServiceAsync(Guid serviceId, CancellationToken ct = default)
    {
        var result = await _ctx.WebhookDeliveries.DeleteManyAsync(
            Builders<WebhookDelivery>.Filter.Eq(d => d.RelatedAccountId, serviceId), ct);
        return result.DeletedCount;
    }

    /// <inheritdoc />
    public async Task<long> PurgeProcessedOlderThanAsync(DateTime olderThanUtc, CancellationToken ct = default)
    {
        var fb = Builders<WebhookDelivery>.Filter;
        var filter = fb.And(
            fb.In(d => d.Status, new[] { WebhookDeliveryStatus.Sent, WebhookDeliveryStatus.Failed }),
            fb.Lt(d => d.CreatedAt, olderThanUtc));
        var result = await _ctx.WebhookDeliveries.DeleteManyAsync(filter, ct);
        return result.DeletedCount;
    }
}
