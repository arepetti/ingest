using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Mongo;

/// <summary>
/// MongoDB-backed implementation of <see cref="ISchemaVersionHistoryRepository"/>. Stores snapshots
/// in the <c>schemaVersionHistories</c> collection. Every read path sorts by
/// <see cref="SchemaVersionHistory.ChangeDate"/> descending so the most recent save is first. Does
/// not extend <see cref="RepositoryBase{T}"/> because the snapshot is not an
/// <see cref="AuditedEntity"/> and is hard-deleted (never soft-deleted) on admin cleanup.
/// </summary>
public sealed class SchemaVersionHistoryRepository : ISchemaVersionHistoryRepository
{
    private readonly IMongoCollection<SchemaVersionHistory> _collection;

    /// <summary>Create a new repository.</summary>
    /// <param name="ctx">Mongo context.</param>
    public SchemaVersionHistoryRepository(MongoContext ctx) => _collection = ctx.SchemaVersionHistories;

    private static readonly SortDefinition<SchemaVersionHistory> NewestFirst =
        Builders<SchemaVersionHistory>.Sort.Descending(h => h.ChangeDate);

    /// <inheritdoc />
    public Task AddAsync(SchemaVersionHistory entry, CancellationToken ct = default) =>
        _collection.InsertOneAsync(entry, cancellationToken: ct);

    /// <inheritdoc />
    public async Task<PagedResult<SchemaVersionHistory>> ListAsync(
        string schemaName,
        PageRequest request,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default)
    {
        var fb = Builders<SchemaVersionHistory>.Filter;
        var filter = fb.Eq(h => h.SchemaName, schemaName);
        // from inclusive, to exclusive (mirrors the audit log); coerce to UTC for the comparison.
        if (from is { } f)
            filter &= fb.Gte(h => h.ChangeDate, DateTime.SpecifyKind(f, DateTimeKind.Utc));
        if (to is { } t)
            filter &= fb.Lt(h => h.ChangeDate, DateTime.SpecifyKind(t, DateTimeKind.Utc));

        var total = await _collection.CountDocumentsAsync(filter, cancellationToken: ct);
        var items = await _collection.Find(filter)
            .Sort(NewestFirst)
            .Skip(request.Skip)
            .Limit(request.Take)
            .ToListAsync(ct);
        return new PagedResult<SchemaVersionHistory>(items, total, request.Page, request.PageSize);
    }

    /// <inheritdoc />
    public Task<SchemaVersionHistory?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _collection.Find(h => h.Id == id).FirstOrDefaultAsync(ct)!;

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var result = await _collection.DeleteOneAsync(h => h.Id == id, ct);
        return result.DeletedCount > 0;
    }

    /// <inheritdoc />
    public async Task<long> DeleteAllForSchemaAsync(string schemaName, CancellationToken ct = default)
    {
        var result = await _collection.DeleteManyAsync(h => h.SchemaName == schemaName, ct);
        return result.DeletedCount;
    }
}
