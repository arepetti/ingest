using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace Ingest.Infrastructure.Mongo;

/// <summary>
/// MongoDB-backed implementation of <see cref="ISampleRepository"/>. Owns the denormalised
/// <c>samples</c> collection — one document per submitted sample — that powers the OData feed,
/// the admin query endpoint and the status calculator.
/// </summary>
public sealed class SampleRepository : RepositoryBase<SampleProjection>, ISampleRepository
{
    /// <summary>Create a new repository.</summary>
    /// <param name="ctx">Mongo context.</param>
    /// <param name="audit">Audit context.</param>
    public SampleRepository(MongoContext ctx, IAuditContext audit) : base(ctx.Samples, audit) { }

    /// <inheritdoc />
    public async Task<PagedResult<SampleProjection>> QueryAsync(SampleQuery q, CancellationToken ct = default)
    {
        var filter = ApplySoftDelete(Builders<SampleProjection>.Filter.Empty, q.IncludeDeleted);

        if (q.ServiceIds is { Count: > 0 })
            filter = Builders<SampleProjection>.Filter.And(filter, Builders<SampleProjection>.Filter.In(s => s.ServiceAccountId, q.ServiceIds));
        if (q.SchemaNames is { Count: > 0 })
            filter = Builders<SampleProjection>.Filter.And(filter, Builders<SampleProjection>.Filter.In(s => s.SchemaName, q.SchemaNames));
        if (q.From is { } from)
            filter = Builders<SampleProjection>.Filter.And(filter, Builders<SampleProjection>.Filter.Gte(s => s.Timestamp, from));
        if (q.To is { } to)
            filter = Builders<SampleProjection>.Filter.And(filter, Builders<SampleProjection>.Filter.Lt(s => s.Timestamp, to));

        long total;
        IReadOnlyList<SampleProjection> items;

        if (q.LatestOnly)
        {
            // Latest per (service, schema). Cheap enough for PoC: load filtered, group in memory.
            var loaded = await Collection.Find(filter)
                .Sort(Builders<SampleProjection>.Sort.Descending(s => s.Timestamp))
                .Limit(10_000)
                .ToListAsync(ct);
            var grouped = loaded
                .GroupBy(s => (s.ServiceAccountId, s.SchemaName))
                .Select(g => g.First())
                .ToList();
            total = grouped.Count;
            items = grouped
                .Skip(Math.Max(0, (q.Page - 1) * q.PageSize))
                .Take(Math.Clamp(q.PageSize, 1, 500))
                .ToList();
        }
        else
        {
            total = await Collection.CountDocumentsAsync(filter, cancellationToken: ct);
            items = await Collection.Find(filter)
                .Sort(Builders<SampleProjection>.Sort.Descending(s => s.Timestamp))
                .Skip(Math.Max(0, (q.Page - 1) * q.PageSize))
                .Limit(Math.Clamp(q.PageSize, 1, 500))
                .ToListAsync(ct);
        }

        return new PagedResult<SampleProjection>(items, total, q.Page, q.PageSize);
    }

    /// <inheritdoc />
    public Task<SampleProjection?> GetLatestAsync(Guid serviceId, string schemaName, string valueName, CancellationToken ct = default)
    {
        var filter = Builders<SampleProjection>.Filter.And(
            NotDeleted,
            Builders<SampleProjection>.Filter.Eq(s => s.ServiceAccountId, serviceId),
            Builders<SampleProjection>.Filter.Eq(s => s.SchemaName, schemaName),
            Builders<SampleProjection>.Filter.Eq(s => s.ValueName, valueName));
        return Collection.Find(filter)
            .Sort(Builders<SampleProjection>.Sort.Descending(s => s.Timestamp))
            .FirstOrDefaultAsync(ct)!;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SampleProjection>> GetAllForSchemaAsync(string schemaName, CancellationToken ct = default)
    {
        var filter = Builders<SampleProjection>.Filter.And(
            NotDeleted,
            Builders<SampleProjection>.Filter.Eq(s => s.SchemaName, schemaName));
        return await Collection.Find(filter)
            .Sort(Builders<SampleProjection>.Sort.Ascending(s => s.PeriodStart))
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task ReplaceForSubmissionAsync(Guid submissionId, IEnumerable<SampleProjection> projections, CancellationToken ct = default)
    {
        await Collection.DeleteManyAsync(s => s.SubmissionId == submissionId, ct);
        var list = projections.ToList();
        if (list.Count == 0) return;
        foreach (var p in list) StampForCreate(p);
        await Collection.InsertManyAsync(list, cancellationToken: ct);
    }

    /// <inheritdoc />
    public Task SoftDeleteForSubmissionAsync(Guid submissionId, CancellationToken ct = default)
    {
        var update = Builders<SampleProjection>.Update
            .Set(s => s.IsDeleted, true)
            .Set(s => s.DeletedAt, Audit.UtcNow)
            .Set(s => s.DeletedBy, Audit.UserName)
            .Set(s => s.ModifiedAt, Audit.UtcNow)
            .Set(s => s.ModifiedBy, Audit.UserName);
        return Collection.UpdateManyAsync(s => s.SubmissionId == submissionId, update, cancellationToken: ct);
    }

    /// <inheritdoc />
    public Task<bool> IsSchemaInUseAsync(string schemaName, CancellationToken ct = default)
    {
        // `AnyAsync` over `Find().Limit(1)` stops the driver as soon as one matching document is
        // located — no scan of the full collection, no count to compute. The compound
        // `by_service_schema_value_time` index isn't a perfect fit for a SchemaName-only filter
        // (the prefix is ServiceAccountId), but at PoC scale a bounded match-or-miss query is
        // fast enough that adding a dedicated index isn't worth the write cost.
        var filter = Builders<SampleProjection>.Filter.And(
            NotDeleted,
            Builders<SampleProjection>.Filter.Eq(s => s.SchemaName, schemaName));
        return Collection.Find(filter).Limit(1).AnyAsync(ct);
    }

    /// <inheritdoc />
    public Task<bool> IsAccountInUseAsync(Guid serviceAccountId, CancellationToken ct = default)
    {
        // ServiceAccountId is the leading column of `by_service_schema_value_time`, so this is a
        // direct index hit even before the Limit(1) short-circuit kicks in.
        var filter = Builders<SampleProjection>.Filter.And(
            NotDeleted,
            Builders<SampleProjection>.Filter.Eq(s => s.ServiceAccountId, serviceAccountId));
        return Collection.Find(filter).Limit(1).AnyAsync(ct);
    }

    /// <inheritdoc />
    public IQueryable<SampleProjection> AsQueryable()
        => Collection.AsQueryable().Where(s => !s.IsDeleted);
}
