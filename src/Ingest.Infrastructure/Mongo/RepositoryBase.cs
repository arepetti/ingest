using Ingest.Core.Common;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Mongo;

/// <summary>
/// Shared plumbing for Mongo repositories on <see cref="AuditedEntity"/> aggregates: the typed
/// collection handle, the audit context, soft-delete filtering helpers, and the create/update/
/// delete audit-stamp routines.
/// </summary>
/// <typeparam name="T">Concrete entity type managed by the repository.</typeparam>
public abstract class RepositoryBase<T> where T : AuditedEntity
{
    /// <summary>The underlying Mongo collection.</summary>
    protected readonly IMongoCollection<T> Collection;

    /// <summary>Ambient "who/when" context used to stamp audit fields.</summary>
    protected readonly IAuditContext Audit;

    /// <summary>Create a new repository bound to <paramref name="collection"/>.</summary>
    /// <param name="collection">Mongo collection backing this repository.</param>
    /// <param name="audit">Audit context.</param>
    protected RepositoryBase(IMongoCollection<T> collection, IAuditContext audit)
    {
        Collection = collection;
        Audit = audit;
    }

    /// <summary>Filter that matches non-soft-deleted rows. Pre-built and reused.</summary>
    protected static FilterDefinition<T> NotDeleted =>
        Builders<T>.Filter.Eq(e => e.IsDeleted, false);

    /// <summary>
    /// Combine an arbitrary filter with the soft-delete guard, unless <paramref name="includeDeleted"/>
    /// asks for everything.
    /// </summary>
    /// <param name="filter">Base filter.</param>
    /// <param name="includeDeleted">When true, the soft-delete guard is bypassed.</param>
    protected FilterDefinition<T> ApplySoftDelete(FilterDefinition<T> filter, bool includeDeleted)
        => includeDeleted ? filter : Builders<T>.Filter.And(filter, NotDeleted);

    /// <summary>Populate the create-time audit fields on a fresh entity. Idempotent.</summary>
    protected void StampForCreate(T entity)
    {
        entity.CreatedAt = Audit.UtcNow;
        entity.ModifiedAt = Audit.UtcNow;
        entity.CreatedBy = Audit.UserName;
        entity.ModifiedBy = Audit.UserName;
        entity.IsDeleted = false;
    }

    /// <summary>Update the modify-time audit fields on an existing entity.</summary>
    protected void StampForUpdate(T entity)
    {
        entity.ModifiedAt = Audit.UtcNow;
        entity.ModifiedBy = Audit.UserName;
    }

    /// <summary>
    /// Apply the soft-delete update to the entity with the given <paramref name="id"/>. Issues a
    /// single Mongo update; no-op when the row doesn't exist or is already deleted.
    /// </summary>
    /// <param name="id">Primary key of the row to soft-delete.</param>
    /// <param name="ct">Cancellation token.</param>
    protected async Task SoftDeleteCoreAsync(Guid id, CancellationToken ct)
    {
        var update = Builders<T>.Update
            .Set(e => e.IsDeleted, true)
            .Set(e => e.DeletedAt, Audit.UtcNow)
            .Set(e => e.DeletedBy, Audit.UserName)
            .Set(e => e.ModifiedAt, Audit.UtcNow)
            .Set(e => e.ModifiedBy, Audit.UserName);
        await Collection.UpdateOneAsync(e => e.Id == id, update, cancellationToken: ct);
    }
}
