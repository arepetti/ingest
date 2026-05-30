using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Mongo;

/// <summary>
/// MongoDB-backed implementation of <see cref="IReportRepository"/>. Reports live in the
/// <c>reports</c> collection with a unique index on <see cref="Report.Name"/>; soft-deletion is
/// honoured by the inherited <see cref="RepositoryBase{T}"/> helpers.
/// </summary>
public sealed class ReportRepository : RepositoryBase<Report>, IReportRepository
{
    /// <summary>Create a new repository.</summary>
    /// <param name="ctx">Mongo context.</param>
    /// <param name="audit">Audit context.</param>
    public ReportRepository(MongoContext ctx, IAuditContext audit) : base(ctx.Reports, audit) { }

    /// <inheritdoc />
    public Task<Report?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default)
    {
        var filter = ApplySoftDelete(Builders<Report>.Filter.Eq(r => r.Id, id), includeDeleted);
        return Collection.Find(filter).FirstOrDefaultAsync(ct)!;
    }

    /// <inheritdoc />
    public Task<Report?> GetByNameAsync(string name, bool includeDeleted = false, CancellationToken ct = default)
    {
        var filter = ApplySoftDelete(Builders<Report>.Filter.Eq(r => r.Name, name), includeDeleted);
        return Collection.Find(filter).FirstOrDefaultAsync(ct)!;
    }

    /// <inheritdoc />
    public async Task<PagedResult<Report>> ListAsync(PageRequest request, CancellationToken ct = default)
    {
        var filter = ApplySoftDelete(Builders<Report>.Filter.Empty, request.IncludeDeleted);
        var total = await Collection.CountDocumentsAsync(filter, cancellationToken: ct);

        var sort = string.Equals(request.Sort, "createdAt", StringComparison.OrdinalIgnoreCase)
            ? Builders<Report>.Sort.Descending(r => r.CreatedAt)
            : Builders<Report>.Sort.Combine(
                Builders<Report>.Sort.Ascending(r => r.Label),
                Builders<Report>.Sort.Ascending(r => r.Name));

        var items = await Collection.Find(filter)
            .Sort(sort)
            .Skip(request.Skip)
            .Limit(request.Take)
            .ToListAsync(ct);

        return new PagedResult<Report>(items, total, request.Page, request.PageSize);
    }

    /// <inheritdoc />
    public Task AddAsync(Report report, CancellationToken ct = default)
    {
        StampForCreate(report);
        return Collection.InsertOneAsync(report, cancellationToken: ct);
    }

    /// <inheritdoc />
    public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => SoftDeleteCoreAsync(id, ct);
}
