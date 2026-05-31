using Ingest.Core.Common;
using Ingest.Core.Entities;

namespace Ingest.Core.Abstractions;

/// <summary>
/// Persistence boundary for <see cref="Account"/> aggregates. Implementations must honour the
/// soft-delete flag — by default deleted accounts are invisible to lookups, but callers can opt
/// them in via <c>includeDeleted</c>.
/// </summary>
public interface IAccountRepository
{
    /// <summary>Fetch an account by id.</summary>
    /// <param name="id">Account id.</param>
    /// <param name="includeDeleted">When true, soft-deleted accounts are also considered.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The account, or <c>null</c> if no match exists.</returns>
    Task<Account?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default);

    /// <summary>Fetch an account by its unique machine-style name.</summary>
    /// <param name="name">Account name (case-sensitive).</param>
    /// <param name="includeDeleted">When true, soft-deleted accounts are also considered.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The account, or <c>null</c> if no match.</returns>
    Task<Account?> GetByNameAsync(string name, bool includeDeleted = false, CancellationToken ct = default);

    /// <summary>Page through accounts, optionally restricted by kind/role.</summary>
    /// <param name="request">Paging + sort parameters.</param>
    /// <param name="kind">Filter by kind when set.</param>
    /// <param name="role">Filter by role when set.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A page of accounts together with the total count.</returns>
    Task<PagedResult<Account>> ListAsync(PageRequest request, AccountKind? kind = null, AccountRole? role = null, CancellationToken ct = default);

    /// <summary>Insert a new account record. The id must already be set.</summary>
    /// <param name="account">Account to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AddAsync(Account account, CancellationToken ct = default);

    /// <summary>Replace an existing account record by id.</summary>
    /// <param name="account">Account with the new field values.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateAsync(Account account, CancellationToken ct = default);

    /// <summary>Flip the soft-delete flag on an account. Idempotent.</summary>
    /// <param name="id">Account id.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SoftDeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Permanently remove an account row from the collection. Used only to free up the unique
    /// name slot held by a previously soft-deleted account so a fresh create can reuse the same
    /// machine name — never exposed to public delete endpoints.
    /// </summary>
    /// <param name="id">Account id.</param>
    /// <param name="ct">Cancellation token.</param>
    Task HardDeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// Persistence boundary for <see cref="ApiKey"/> entities. Only metadata is stored (id, prefix,
/// salt+hash, timestamps); the plaintext lives only in the issuance response.
/// </summary>
public interface IApiKeyRepository
{
    /// <summary>Fetch a key by its public id portion (used by authentication to locate the salt+hash).</summary>
    /// <param name="keyId">The id portion of the plaintext (everything before the first dot).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The key entity, or <c>null</c> if no such id exists.</returns>
    Task<ApiKey?> GetByKeyIdAsync(string keyId, CancellationToken ct = default);

    /// <summary>List the currently-usable (non-revoked) keys for an account.</summary>
    /// <param name="accountId">Account id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Active keys; empty if none.</returns>
    Task<IReadOnlyList<ApiKey>> GetActiveByAccountAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>List every key (including revoked) for an account.</summary>
    /// <param name="accountId">Account id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All keys for the account; empty if none.</returns>
    Task<IReadOnlyList<ApiKey>> ListByAccountAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>Insert a new key.</summary>
    /// <param name="key">Key to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AddAsync(ApiKey key, CancellationToken ct = default);

    /// <summary>Replace an existing key by id (used to set the revocation timestamp).</summary>
    /// <param name="key">Key with the new field values.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateAsync(ApiKey key, CancellationToken ct = default);
}

/// <summary>Persistence boundary for <see cref="Schema"/> aggregates.</summary>
public interface ISchemaRepository
{
    /// <summary>Fetch a schema by id.</summary>
    /// <param name="id">Schema id.</param>
    /// <param name="includeDeleted">When true, soft-deleted schemas are considered.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The schema, or <c>null</c>.</returns>
    Task<Schema?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default);

    /// <summary>Fetch a schema by its unique machine-style name.</summary>
    /// <param name="name">Schema name (case-sensitive).</param>
    /// <param name="includeDeleted">When true, soft-deleted schemas are considered.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The schema, or <c>null</c>.</returns>
    Task<Schema?> GetByNameAsync(string name, bool includeDeleted = false, CancellationToken ct = default);

    /// <summary>
    /// Return every live schema visible to a service account (global, or with the account in
    /// <see cref="Schema.ServiceIds"/>). Soft-deleted schemas are excluded.
    /// </summary>
    /// <param name="serviceId">Account id to test visibility for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The visible schemas.</returns>
    Task<IReadOnlyList<Schema>> ListVisibleToAsync(Guid serviceId, CancellationToken ct = default);

    /// <summary>Page through every schema in the catalogue.</summary>
    /// <param name="request">Paging + sort parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A page of schemas with the total count.</returns>
    Task<PagedResult<Schema>> ListAsync(PageRequest request, CancellationToken ct = default);

    /// <summary>Insert a new schema.</summary>
    /// <param name="schema">Schema to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AddAsync(Schema schema, CancellationToken ct = default);

    /// <summary>Replace an existing schema by id.</summary>
    /// <param name="schema">Schema with the new field values.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateAsync(Schema schema, CancellationToken ct = default);

    /// <summary>Flip the soft-delete flag on a schema. Idempotent.</summary>
    /// <param name="id">Schema id.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SoftDeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Permanently remove a schema row from the collection. Used only to free up the unique
    /// name slot held by a previously soft-deleted schema so a fresh create (or a rename) can
    /// reuse the same machine name — never exposed to public delete endpoints.
    /// </summary>
    /// <param name="id">Schema id.</param>
    /// <param name="ct">Cancellation token.</param>
    Task HardDeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Persistence boundary for <see cref="Submission"/> aggregates.</summary>
public interface ISubmissionRepository
{
    /// <summary>Fetch a submission by id.</summary>
    /// <param name="id">Submission id.</param>
    /// <param name="includeDeleted">When true, soft-deleted submissions are also considered.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The submission, or <c>null</c>.</returns>
    Task<Submission?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default);

    /// <summary>Page through submissions with optional service/schema/date filters.</summary>
    /// <param name="request">Paging + sort parameters; <c>IncludeDeleted</c> opts soft-deleted ones in.</param>
    /// <param name="serviceId">Restrict to a single account when non-null.</param>
    /// <param name="from">Lower bound on submission timestamp (inclusive).</param>
    /// <param name="to">Upper bound on submission timestamp (exclusive).</param>
    /// <param name="schemaName">Restrict to submissions containing at least one sample for this schema when non-null.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A page of submissions with the total count.</returns>
    Task<PagedResult<Submission>> ListAsync(
        PageRequest request,
        Guid? serviceId = null,
        DateTime? from = null,
        DateTime? to = null,
        string? schemaName = null,
        CancellationToken ct = default);

    /// <summary>Insert a new submission.</summary>
    /// <param name="submission">Submission to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AddAsync(Submission submission, CancellationToken ct = default);

    /// <summary>Replace an existing submission by id.</summary>
    /// <param name="submission">Submission with the new field values.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateAsync(Submission submission, CancellationToken ct = default);

    /// <summary>Flip the soft-delete flag on a submission. Idempotent.</summary>
    /// <param name="id">Submission id.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SoftDeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Filter shape for the flat sample projection query.</summary>
/// <param name="ServiceIds">Restrict to submissions from any of these accounts.</param>
/// <param name="SchemaNames">Restrict to samples for any of these schemas.</param>
/// <param name="From">Lower bound on sample timestamp (inclusive).</param>
/// <param name="To">Upper bound on sample timestamp (exclusive).</param>
/// <param name="LatestOnly">When true, return only the most recent sample per (service, schema, value) tuple.</param>
/// <param name="IncludeDeleted">When true, soft-deleted samples are included.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Page size.</param>
/// <param name="Sort">Optional sort hint; backend may interpret <c>timestamp</c>, etc.</param>
public sealed record SampleQuery(
    IReadOnlyList<Guid>? ServiceIds,
    IReadOnlyList<string>? SchemaNames,
    DateTime? From,
    DateTime? To,
    bool LatestOnly,
    bool IncludeDeleted,
    int Page,
    int PageSize,
    string? Sort);

/// <summary>
/// Persistence boundary for <see cref="SampleProjection"/>: a denormalised flat view of every
/// sample inside every submission, designed so reporting tools (PowerBI through the OData feed,
/// dashboards through the admin query endpoint) can scan it without joining submissions, schemas
/// and accounts.
/// </summary>
public interface ISampleRepository
{
    /// <summary>Page through the projection using the supplied filters.</summary>
    /// <param name="query">Filter + paging shape.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A page of projections with the total count.</returns>
    Task<PagedResult<SampleProjection>> QueryAsync(SampleQuery query, CancellationToken ct = default);

    /// <summary>Fetch the most recent projection row for a single (service, schema, value) tuple.</summary>
    /// <param name="serviceId">Owning service account id.</param>
    /// <param name="schemaName">Machine-style schema name.</param>
    /// <param name="valueName">Machine-style value name inside that schema.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The latest projection, or <c>null</c> if none exists.</returns>
    Task<SampleProjection?> GetLatestAsync(Guid serviceId, string schemaName, string valueName, CancellationToken ct = default);

    /// <summary>Return every non-deleted projection for a schema. Used by the schema history aggregation.</summary>
    /// <param name="schemaName">Machine-style schema name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Every live projection for the schema.</returns>
    Task<IReadOnlyList<SampleProjection>> GetAllForSchemaAsync(string schemaName, CancellationToken ct = default);

    /// <summary>Atomically replace every projection belonging to a submission. Used on create/update.</summary>
    /// <param name="submissionId">Submission whose projections should be rebuilt.</param>
    /// <param name="projections">The new projection rows.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ReplaceForSubmissionAsync(Guid submissionId, IEnumerable<SampleProjection> projections, CancellationToken ct = default);

    /// <summary>Soft-delete every projection belonging to a submission. Used when the parent submission is deleted.</summary>
    /// <param name="submissionId">Submission whose projections should be removed from queries.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SoftDeleteForSubmissionAsync(Guid submissionId, CancellationToken ct = default);

    /// <summary>
    /// True when at least one live (non-deleted) projection references the named schema. Used by
    /// the schema delete path to block destructive deletes when historical data still depends on
    /// the definition — the caller is expected to disable the schema instead.
    /// </summary>
    /// <param name="schemaName">Machine-style schema name to test.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if any live sample uses this schema; <c>false</c> otherwise.</returns>
    Task<bool> IsSchemaInUseAsync(string schemaName, CancellationToken ct = default);

    /// <summary>
    /// True when at least one live (non-deleted) projection was submitted by the given service
    /// account. Used by the account delete path to block deletes that would orphan historical
    /// data — the caller is expected to disable the account instead.
    /// </summary>
    /// <param name="serviceAccountId">Service-account id to test.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if any live sample originated from this account; <c>false</c> otherwise.</returns>
    Task<bool> IsAccountInUseAsync(Guid serviceAccountId, CancellationToken ct = default);

    /// <summary>Expose the projection store as <see cref="IQueryable{T}"/> for OData/LINQ consumers.</summary>
    /// <returns>An <see cref="IQueryable{SampleProjection}"/> bound to the underlying store.</returns>
    IQueryable<SampleProjection> AsQueryable();
}
