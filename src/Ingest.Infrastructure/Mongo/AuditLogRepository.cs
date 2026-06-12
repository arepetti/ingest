using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Mongo;

/// <summary>
/// MongoDB-backed implementation of <see cref="IAuditLogRepository"/>. Stores entries in the
/// <c>auditLogs</c> collection. Every read path sorts by <see cref="AuditLog.Timestamp"/>
/// descending so the most recent change is always first. Does not extend
/// <see cref="RepositoryBase{T}"/> because the log is append-only and never soft-deleted.
/// </summary>
public sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly IMongoCollection<AuditLog> _collection;

    /// <summary>Create a new repository.</summary>
    /// <param name="ctx">Mongo context.</param>
    public AuditLogRepository(MongoContext ctx) => _collection = ctx.AuditLogs;

    private static readonly SortDefinition<AuditLog> NewestFirst =
        Builders<AuditLog>.Sort.Descending(a => a.Timestamp);

    /// <inheritdoc />
    public Task AddAsync(AuditLog entry, CancellationToken ct = default) =>
        _collection.InsertOneAsync(entry, cancellationToken: ct);

    /// <inheritdoc />
    public async Task<PagedResult<AuditLog>> ListAsync(
        PageRequest request,
        AuditChangeType? change = null,
        AuditTargetType? targetType = null,
        string? nameFilter = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default)
    {
        var filter = BuildFilter(change, targetType, nameFilter, from, to);
        var total = await _collection.CountDocumentsAsync(filter, cancellationToken: ct);
        var items = await _collection.Find(filter)
            .Sort(NewestFirst)
            .Skip(request.Skip)
            .Limit(request.Take)
            .ToListAsync(ct);
        return new PagedResult<AuditLog>(items, total, request.Page, request.PageSize);
    }

    /// <inheritdoc />
    public async Task<PagedResult<AuditLog>> ListByTargetAsync(Guid targetId, PageRequest request, CancellationToken ct = default)
    {
        var filter = Builders<AuditLog>.Filter.Eq(a => a.TargetId, targetId);
        var total = await _collection.CountDocumentsAsync(filter, cancellationToken: ct);
        var items = await _collection.Find(filter)
            .Sort(NewestFirst)
            .Skip(request.Skip)
            .Limit(request.Take)
            .ToListAsync(ct);
        return new PagedResult<AuditLog>(items, total, request.Page, request.PageSize);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AuditLog> StreamAsync(
        AuditChangeType? change = null,
        AuditTargetType? targetType = null,
        string? nameFilter = null,
        DateTime? from = null,
        DateTime? to = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var filter = BuildFilter(change, targetType, nameFilter, from, to);
        using var cursor = await _collection.Find(filter).Sort(NewestFirst).ToCursorAsync(ct);
        while (await cursor.MoveNextAsync(ct))
        {
            foreach (var entry in cursor.Current)
                yield return entry;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditLog>> ListForAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        var fb = Builders<AuditLog>.Filter;
        var filter = fb.Or(fb.Eq(a => a.TargetId, accountId), fb.Eq(a => a.ActorId, accountId));
        return await _collection.Find(filter).Sort(NewestFirst).ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<long> AnonymiseAccountAsync(Guid accountId, string pseudonym, CancellationToken ct = default)
    {
        var fb = Builders<AuditLog>.Filter;
        var asTarget = await _collection.UpdateManyAsync(
            fb.Eq(a => a.TargetId, accountId),
            Builders<AuditLog>.Update.Set(a => a.TargetName, pseudonym),
            cancellationToken: ct);
        var asActor = await _collection.UpdateManyAsync(
            fb.Eq(a => a.ActorId, accountId),
            Builders<AuditLog>.Update.Set(a => a.ActorName, pseudonym),
            cancellationToken: ct);
        return asTarget.ModifiedCount + asActor.ModifiedCount;
    }

    /// <inheritdoc />
    public async Task<long> HardDeleteForAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        var fb = Builders<AuditLog>.Filter;
        var filter = fb.Or(fb.Eq(a => a.TargetId, accountId), fb.Eq(a => a.ActorId, accountId));
        var result = await _collection.DeleteManyAsync(filter, ct);
        return result.DeletedCount;
    }

    /// <inheritdoc />
    public async Task<long> PurgeOlderThanAsync(DateTime olderThanUtc, CancellationToken ct = default)
    {
        var result = await _collection.DeleteManyAsync(
            Builders<AuditLog>.Filter.Lt(a => a.Timestamp, olderThanUtc), ct);
        return result.DeletedCount;
    }

    /// <summary>Build the AND-combined filter shared by the list and export paths. Empty when no filter is supplied.</summary>
    private static FilterDefinition<AuditLog> BuildFilter(
        AuditChangeType? change, AuditTargetType? targetType, string? nameFilter, DateTime? from, DateTime? to)
    {
        var fb = Builders<AuditLog>.Filter;
        var filter = fb.Empty;
        if (change is { } c)
            filter &= fb.Eq(a => a.Change, c);
        if (targetType is { } t)
            filter &= fb.Eq(a => a.TargetType, t);
        if (!string.IsNullOrWhiteSpace(nameFilter))
        {
            var rx = new BsonRegularExpression(Regex.Escape(nameFilter.Trim()), "i");
            filter &= fb.Or(fb.Regex(a => a.TargetName, rx), fb.Regex(a => a.ActorName, rx));
        }
        // from inclusive, to exclusive (mirrors the submissions list); coerce to UTC so the
        // comparison matches how timestamps are stored.
        if (from is { } f)
            filter &= fb.Gte(a => a.Timestamp, DateTime.SpecifyKind(f, DateTimeKind.Utc));
        if (to is { } u)
            filter &= fb.Lt(a => a.Timestamp, DateTime.SpecifyKind(u, DateTimeKind.Utc));
        return filter;
    }
}
