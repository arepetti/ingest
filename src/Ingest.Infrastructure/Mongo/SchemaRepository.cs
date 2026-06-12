using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Mongo;

/// <summary>
/// MongoDB-backed implementation of <see cref="ISchemaRepository"/>. Stores schemas in the
/// <c>schemas</c> collection. The audience filter on <see cref="ListVisibleToAsync"/> matches
/// either <c>isGlobal=true</c> or membership in <c>serviceIds</c>.
/// </summary>
public sealed class SchemaRepository : RepositoryBase<Schema>, ISchemaRepository
{
    /// <summary>Create a new repository.</summary>
    /// <param name="ctx">Mongo context.</param>
    /// <param name="audit">Audit context.</param>
    public SchemaRepository(MongoContext ctx, IAuditContext audit) : base(ctx.Schemas, audit) { }

    /// <inheritdoc />
    public Task<Schema?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default)
    {
        var filter = ApplySoftDelete(Builders<Schema>.Filter.Eq(s => s.Id, id), includeDeleted);
        return Collection.Find(filter).FirstOrDefaultAsync(ct)!;
    }

    /// <inheritdoc />
    public Task<Schema?> GetByNameAsync(string name, bool includeDeleted = false, CancellationToken ct = default)
    {
        var filter = ApplySoftDelete(Builders<Schema>.Filter.Eq(s => s.Name, name), includeDeleted);
        return Collection.Find(filter).FirstOrDefaultAsync(ct)!;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Schema>> ListVisibleToAsync(Guid serviceId, CancellationToken ct = default)
    {
        var filter = Builders<Schema>.Filter.And(
            NotDeleted,
            Builders<Schema>.Filter.Or(
                Builders<Schema>.Filter.Eq(s => s.IsGlobal, true),
                Builders<Schema>.Filter.AnyEq(s => s.ServiceIds, serviceId)));
        return await Collection.Find(filter)
            .Sort(Builders<Schema>.Sort.Combine(
                Builders<Schema>.Sort.Ascending(s => s.Label),
                Builders<Schema>.Sort.Ascending(s => s.Name)))
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<PagedResult<Schema>> ListAsync(PageRequest request, CancellationToken ct = default)
    {
        var filter = ApplySoftDelete(Builders<Schema>.Filter.Empty, request.IncludeDeleted);
        var total = await Collection.CountDocumentsAsync(filter, cancellationToken: ct);

        var sort = string.Equals(request.Sort, "createdAt", StringComparison.OrdinalIgnoreCase)
            ? Builders<Schema>.Sort.Descending(s => s.CreatedAt)
            : Builders<Schema>.Sort.Combine(
                Builders<Schema>.Sort.Ascending(s => s.Label),
                Builders<Schema>.Sort.Ascending(s => s.Name));

        var items = await Collection.Find(filter)
            .Sort(sort)
            .Skip(request.Skip)
            .Limit(request.Take)
            .ToListAsync(ct);

        return new PagedResult<Schema>(items, total, request.Page, request.PageSize);
    }

    /// <inheritdoc />
    public Task AddAsync(Schema schema, CancellationToken ct = default)
    {
        StampForCreate(schema);
        return Collection.InsertOneAsync(schema, cancellationToken: ct);
    }

    /// <inheritdoc />
    public Task UpdateAsync(Schema schema, CancellationToken ct = default)
    {
        StampForUpdate(schema);
        return Collection.ReplaceOneAsync(s => s.Id == schema.Id, schema, cancellationToken: ct);
    }

    /// <inheritdoc />
    public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => SoftDeleteCoreAsync(id, ct);

    /// <inheritdoc />
    public Task HardDeleteAsync(Guid id, CancellationToken ct = default) =>
        Collection.DeleteOneAsync(s => s.Id == id, ct);

    /// <inheritdoc />
    public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) =>
        PurgeSoftDeletedCoreAsync(olderThanUtc, ct);
}
