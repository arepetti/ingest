using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Mongo;

/// <summary>
/// MongoDB-backed implementation of <see cref="IAccountRepository"/>. Stores accounts in the
/// <c>accounts</c> collection. The list endpoint sorts by <c>label</c> then <c>name</c> by default
/// to keep the admin UI alphabetical regardless of whether labels are present.
/// </summary>
public sealed class AccountRepository : RepositoryBase<Account>, IAccountRepository
{
    /// <summary>Create a new repository.</summary>
    /// <param name="ctx">Mongo context.</param>
    /// <param name="audit">Audit context for stamping.</param>
    public AccountRepository(MongoContext ctx, IAuditContext audit) : base(ctx.Accounts, audit) { }

    /// <inheritdoc />
    public Task<Account?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default)
    {
        var filter = ApplySoftDelete(Builders<Account>.Filter.Eq(a => a.Id, id), includeDeleted);
        return Collection.Find(filter).FirstOrDefaultAsync(ct)!;
    }

    /// <inheritdoc />
    public Task<Account?> GetByNameAsync(string name, bool includeDeleted = false, CancellationToken ct = default)
    {
        var filter = ApplySoftDelete(Builders<Account>.Filter.Eq(a => a.Name, name), includeDeleted);
        return Collection.Find(filter).FirstOrDefaultAsync(ct)!;
    }

    /// <inheritdoc />
    public Task<Account?> GetByExternalLoginAsync(string provider, string email, CancellationToken ct = default)
    {
        // Emails are persisted lower-cased (see AccountService) so a plain equality match is
        // effectively case-insensitive without a regex. Provider ids are stored as-is but are
        // a fixed small vocabulary, so match them case-insensitively to be forgiving.
        var normalizedEmail = (email ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedProvider = (provider ?? string.Empty).Trim();
        var elem = Builders<Account>.Filter.ElemMatch(
            a => a.ExternalLogins,
            Builders<ExternalLogin>.Filter.And(
                Builders<ExternalLogin>.Filter.Regex(e => e.Provider, new MongoDB.Bson.BsonRegularExpression($"^{System.Text.RegularExpressions.Regex.Escape(normalizedProvider)}$", "i")),
                Builders<ExternalLogin>.Filter.Eq(e => e.Email, normalizedEmail)));
        var filter = ApplySoftDelete(elem, includeDeleted: false);
        return Collection.Find(filter).FirstOrDefaultAsync(ct)!;
    }

    /// <inheritdoc />
    public async Task<PagedResult<Account>> ListAsync(PageRequest request, AccountKind? kind = null, AccountRole? role = null, CancellationToken ct = default)
    {
        var filter = ApplySoftDelete(Builders<Account>.Filter.Empty, request.IncludeDeleted);
        if (kind is { } k)
            filter = Builders<Account>.Filter.And(filter, Builders<Account>.Filter.Eq(a => a.Kind, k));
        if (role is { } r)
            filter = Builders<Account>.Filter.And(filter, Builders<Account>.Filter.Eq(a => a.Role, r));

        var total = await Collection.CountDocumentsAsync(filter, cancellationToken: ct);
        var sort = string.Equals(request.Sort, "createdAt", StringComparison.OrdinalIgnoreCase)
            ? Builders<Account>.Sort.Descending(a => a.CreatedAt)
            : Builders<Account>.Sort.Combine(
                Builders<Account>.Sort.Ascending(a => a.Label),
                Builders<Account>.Sort.Ascending(a => a.Name));

        var items = await Collection.Find(filter)
            .Sort(sort)
            .Skip(request.Skip)
            .Limit(request.Take)
            .ToListAsync(ct);

        return new PagedResult<Account>(items, total, request.Page, request.PageSize);
    }

    /// <inheritdoc />
    public Task AddAsync(Account account, CancellationToken ct = default)
    {
        StampForCreate(account);
        return Collection.InsertOneAsync(account, cancellationToken: ct);
    }

    /// <inheritdoc />
    public Task UpdateAsync(Account account, CancellationToken ct = default)
    {
        StampForUpdate(account);
        return Collection.ReplaceOneAsync(a => a.Id == account.Id, account, cancellationToken: ct);
    }

    /// <inheritdoc />
    public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => SoftDeleteCoreAsync(id, ct);

    /// <inheritdoc />
    public Task HardDeleteAsync(Guid id, CancellationToken ct = default) =>
        Collection.DeleteOneAsync(a => a.Id == id, ct);
}
