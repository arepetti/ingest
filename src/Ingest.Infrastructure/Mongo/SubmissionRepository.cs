using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Mongo;

/// <summary>
/// MongoDB-backed implementation of <see cref="ISubmissionRepository"/>. Persists raw submission
/// batches in the <c>submissions</c> collection. The denormalised per-sample read model is
/// maintained separately by <see cref="SampleRepository"/>.
/// </summary>
public sealed class SubmissionRepository : RepositoryBase<Submission>, ISubmissionRepository
{
    /// <summary>Create a new repository.</summary>
    /// <param name="ctx">Mongo context.</param>
    /// <param name="audit">Audit context.</param>
    public SubmissionRepository(MongoContext ctx, IAuditContext audit) : base(ctx.Submissions, audit) { }

    /// <inheritdoc />
    public Task<Submission?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default)
    {
        var filter = ApplySoftDelete(Builders<Submission>.Filter.Eq(s => s.Id, id), includeDeleted);
        return Collection.Find(filter).FirstOrDefaultAsync(ct)!;
    }

    /// <inheritdoc />
    public async Task<PagedResult<Submission>> ListAsync(
        PageRequest request,
        Guid? serviceId = null,
        DateTime? from = null,
        DateTime? to = null,
        string? schemaName = null,
        ApprovalStatus? approvalStatus = null,
        CancellationToken ct = default)
    {
        var filter = ApplySoftDelete(Builders<Submission>.Filter.Empty, request.IncludeDeleted);
        if (serviceId is { } sid)
            filter = Builders<Submission>.Filter.And(filter, Builders<Submission>.Filter.Eq(s => s.ServiceAccountId, sid));
        if (approvalStatus is { } status)
            filter = Builders<Submission>.Filter.And(filter, ApprovalStatusFilter(status));
        if (from is { } f)
            filter = Builders<Submission>.Filter.And(filter, Builders<Submission>.Filter.Gte(s => s.SubmittedAt, DateTime.SpecifyKind(f, DateTimeKind.Utc)));
        if (to is { } t)
            filter = Builders<Submission>.Filter.And(filter, Builders<Submission>.Filter.Lt(s => s.SubmittedAt, DateTime.SpecifyKind(t, DateTimeKind.Utc)));
        // A submission carries samples for (typically) a single schema. Match any submission that
        // has at least one sample for the requested schema.
        if (!string.IsNullOrWhiteSpace(schemaName))
            filter = Builders<Submission>.Filter.And(filter,
                Builders<Submission>.Filter.ElemMatch(s => s.Samples, Builders<Sample>.Filter.Eq(x => x.SchemaName, schemaName)));

        var total = await Collection.CountDocumentsAsync(filter, cancellationToken: ct);

        var items = await Collection.Find(filter)
            .Sort(Builders<Submission>.Sort.Descending(s => s.SubmittedAt))
            .Skip(request.Skip)
            .Limit(request.Take)
            .ToListAsync(ct);

        return new PagedResult<Submission>(items, total, request.Page, request.PageSize);
    }

    /// <inheritdoc />
    public Task<long> CountBySchemaAsync(string schemaName, CancellationToken ct = default)
    {
        // Mirror the schemaName branch of ListAsync: any live submission with at least one sample
        // for this schema counts once.
        var filter = Builders<Submission>.Filter.And(
            ApplySoftDelete(Builders<Submission>.Filter.Empty, includeDeleted: false),
            Builders<Submission>.Filter.ElemMatch(s => s.Samples, Builders<Sample>.Filter.Eq(x => x.SchemaName, schemaName)));
        return Collection.CountDocumentsAsync(filter, cancellationToken: ct);
    }

    /// <inheritdoc />
    public Task<long> CountByApprovalStatusAsync(ApprovalStatus status, CancellationToken ct = default)
    {
        var filter = Builders<Submission>.Filter.And(
            ApplySoftDelete(Builders<Submission>.Filter.Empty, includeDeleted: false),
            ApprovalStatusFilter(status));
        return Collection.CountDocumentsAsync(filter, cancellationToken: ct);
    }

    /// <summary>
    /// Filter for a given approval status. Legacy submissions that predate the approval workflow have
    /// no <c>approvalStatus</c> field at all; an equality match on <see cref="ApprovalStatus.NotRequired"/>
    /// (which serialises to <c>0</c>) wouldn't match them, so we additionally accept documents where the
    /// field is absent — they are effectively "not required" (they were live the moment they landed).
    /// </summary>
    private static FilterDefinition<Submission> ApprovalStatusFilter(ApprovalStatus status) =>
        status == ApprovalStatus.NotRequired
            ? Builders<Submission>.Filter.Or(
                Builders<Submission>.Filter.Eq(s => s.ApprovalStatus, status),
                Builders<Submission>.Filter.Exists(s => s.ApprovalStatus, false))
            : Builders<Submission>.Filter.Eq(s => s.ApprovalStatus, status);

    /// <inheritdoc />
    public Task AddAsync(Submission submission, CancellationToken ct = default)
    {
        StampForCreate(submission);
        // Default to "now", but honour an explicit SubmittedAt set by the caller (e.g. a bulk import
        // back-filling history dates the record to the sample's own timestamp).
        if (submission.SubmittedAt == default)
            submission.SubmittedAt = Audit.UtcNow;
        return Collection.InsertOneAsync(submission, cancellationToken: ct);
    }

    /// <inheritdoc />
    public Task UpdateAsync(Submission submission, CancellationToken ct = default)
    {
        StampForUpdate(submission);
        submission.ReplacedAt = Audit.UtcNow;
        return Collection.ReplaceOneAsync(s => s.Id == submission.Id, submission, cancellationToken: ct);
    }

    /// <inheritdoc />
    public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => SoftDeleteCoreAsync(id, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Submission>> ListByServiceAsync(Guid serviceId, bool includeDeleted = false, CancellationToken ct = default)
    {
        var filter = ApplySoftDelete(Builders<Submission>.Filter.Eq(s => s.ServiceAccountId, serviceId), includeDeleted);
        return await Collection.Find(filter)
            .Sort(Builders<Submission>.Sort.Descending(s => s.SubmittedAt))
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public Task<long> HardDeleteByServiceAsync(Guid serviceId, CancellationToken ct = default) =>
        HardDeleteManyCoreAsync(Builders<Submission>.Filter.Eq(s => s.ServiceAccountId, serviceId), ct);

    /// <inheritdoc />
    public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) =>
        PurgeSoftDeletedCoreAsync(olderThanUtc, ct);
}
