using Ingest.Core.Abstractions;
using Ingest.Core.Entities;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Mongo;

/// <summary>
/// MongoDB-backed implementation of <see cref="INotificationLogRepository"/> over the
/// <c>notificationLogs</c> collection. The notification job writes dedupe markers directly through
/// <see cref="MongoContext"/>; this repository only covers the GDPR erasure and retention removals.
/// </summary>
public sealed class NotificationLogRepository : INotificationLogRepository
{
    private readonly IMongoCollection<NotificationLog> _collection;

    /// <summary>Create a new repository.</summary>
    /// <param name="ctx">Mongo context.</param>
    public NotificationLogRepository(MongoContext ctx) => _collection = ctx.NotificationLogs;

    /// <inheritdoc />
    public async Task<long> HardDeleteForServiceAsync(Guid serviceId, CancellationToken ct = default)
    {
        // Marker keys embed the service id (e.g. "upcoming:{serviceId}:{schema}:..."). Match the
        // raw guid anywhere in the key; the guid is unique enough to avoid false positives.
        var rx = new BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(serviceId.ToString()), "i");
        var result = await _collection.DeleteManyAsync(
            Builders<NotificationLog>.Filter.Regex(n => n.Key, rx), ct);
        return result.DeletedCount;
    }

    /// <inheritdoc />
    public async Task<long> PurgeOlderThanAsync(DateTime olderThanUtc, CancellationToken ct = default)
    {
        var result = await _collection.DeleteManyAsync(
            Builders<NotificationLog>.Filter.Lt(n => n.CreatedAt, olderThanUtc), ct);
        return result.DeletedCount;
    }
}
