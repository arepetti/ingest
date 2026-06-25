using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Mongo;

/// <summary>
/// MongoDB-backed implementation of <see cref="IApiKeyRepository"/>. Persists the hashed key
/// material in the <c>apiKeys</c> collection; the plaintext never reaches Mongo.
/// </summary>
public sealed class ApiKeyRepository : RepositoryBase<ApiKey>, IApiKeyRepository
{
    /// <summary>Create a new repository.</summary>
    /// <param name="ctx">Mongo context.</param>
    /// <param name="audit">Audit context.</param>
    public ApiKeyRepository(MongoContext ctx, IAuditContext audit) : base(ctx.ApiKeys, audit) { }

    /// <inheritdoc />
    public Task<ApiKey?> GetByKeyIdAsync(string keyId, CancellationToken ct = default)
    {
        var filter = ApplySoftDelete(Builders<ApiKey>.Filter.Eq(k => k.KeyId, keyId), includeDeleted: false);
        return Collection.Find(filter).FirstOrDefaultAsync(ct)!;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApiKey>> GetActiveByAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        var now = Audit.UtcNow;
        var filter = Builders<ApiKey>.Filter.And(
            NotDeleted,
            Builders<ApiKey>.Filter.Eq(k => k.AccountId, accountId),
            Builders<ApiKey>.Filter.Eq(k => k.RevokedAt, null),
            Builders<ApiKey>.Filter.Or(
                Builders<ApiKey>.Filter.Eq(k => k.ExpiresAt, null),
                Builders<ApiKey>.Filter.Gt(k => k.ExpiresAt, now)));
        return await Collection.Find(filter).ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApiKey>> ListByAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        var filter = Builders<ApiKey>.Filter.Eq(k => k.AccountId, accountId);
        return await Collection.Find(filter).SortByDescending(k => k.CreatedAt).ToListAsync(ct);
    }

    /// <inheritdoc />
    public Task AddAsync(ApiKey key, CancellationToken ct = default)
    {
        StampForCreate(key);
        return Collection.InsertOneAsync(key, cancellationToken: ct);
    }

    /// <inheritdoc />
    public Task UpdateAsync(ApiKey key, CancellationToken ct = default)
    {
        StampForUpdate(key);
        return Collection.ReplaceOneAsync(k => k.Id == key.Id, key, cancellationToken: ct);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var result = await Collection.DeleteOneAsync(k => k.Id == id, ct);
        return result.DeletedCount > 0;
    }

    /// <inheritdoc />
    public Task<long> HardDeleteByAccountAsync(Guid accountId, CancellationToken ct = default) =>
        HardDeleteManyCoreAsync(Builders<ApiKey>.Filter.Eq(k => k.AccountId, accountId), ct);
}
