using Ingest.Core.Common;
using Ingest.Core.Entities;

namespace Ingest.Core.Abstractions;

/// <summary>One time-bucketed aggregate of a numeric value's samples.</summary>
/// <param name="PeriodStart">Inclusive start of the bucket, aligned to the value's cadence.</param>
/// <param name="PeriodEnd">Exclusive end of the bucket.</param>
/// <param name="Min">Smallest sample observed in the bucket.</param>
/// <param name="Max">Largest sample observed in the bucket.</param>
/// <param name="Average">Arithmetic mean of every sample in the bucket.</param>
/// <param name="Count">Number of samples folded into this bucket.</param>
public sealed record HistoryBucket(
    DateTime PeriodStart,
    DateTime PeriodEnd,
    double Min,
    double Max,
    double Average,
    int Count);

/// <summary>Per-value timeline of numeric samples grouped by the value's cadence.</summary>
/// <param name="ValueName">Machine-style name of the schema value.</param>
/// <param name="Label">Friendly label, if one was set on the schema.</param>
/// <param name="Type">Original value type (only numeric values produce non-empty buckets).</param>
/// <param name="Cadence">The cadence used to group samples into buckets.</param>
/// <param name="Unit">Unit of measure carried on the schema definition.</param>
/// <param name="Buckets">Ordered series of aggregated samples.</param>
public sealed record SchemaValueHistory(
    string ValueName,
    string? Label,
    SchemaValueType Type,
    Cadence Cadence,
    string? Unit,
    IReadOnlyList<HistoryBucket> Buckets);

/// <summary>Aggregated numeric history for an entire schema.</summary>
/// <param name="SchemaName">Machine-style schema name.</param>
/// <param name="Label">Friendly schema label.</param>
/// <param name="Values">Per-value histories; non-numeric values are still included with empty buckets so callers can decide what to render.</param>
public sealed record SchemaHistory(string SchemaName, string? Label, IReadOnlyList<SchemaValueHistory> Values);

/// <summary>
/// Schema catalogue and history aggregation. The service-facing methods
/// (<see cref="ListVisibleToAsync"/> / <see cref="GetVisibleAsync"/>) hide the audience-filtering
/// rule (a schema is visible to a service account when it is global, or when its <c>ServiceIds</c>
/// list explicitly names the account) so controllers don't have to know about it.
/// </summary>
public interface ISchemaService
{
    /// <summary>Return every schema visible to the given service account, sorted alphabetically by name.</summary>
    /// <param name="serviceAccountId">Account to test visibility for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The visible schemas. Empty list if none.</returns>
    Task<IReadOnlyList<Schema>> ListVisibleToAsync(Guid serviceAccountId, CancellationToken ct = default);

    /// <summary>Fetch a single schema by name, gated by the same visibility rule as <see cref="ListVisibleToAsync"/>.</summary>
    /// <param name="serviceAccountId">Account to test visibility for.</param>
    /// <param name="name">Machine-style schema name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The schema, or <c>null</c> if it doesn't exist or isn't visible.</returns>
    Task<Schema?> GetVisibleAsync(Guid serviceAccountId, string name, CancellationToken ct = default);

    /// <summary>Page through every schema in the catalogue (no visibility filtering).</summary>
    /// <param name="request">Paging + sort parameters; <c>IncludeDeleted</c> opts soft-deleted schemas in.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A page of schemas with the total count.</returns>
    Task<PagedResult<Schema>> ListAsync(PageRequest request, CancellationToken ct = default);

    /// <summary>Fetch a schema by id, with optional soft-deletion visibility.</summary>
    /// <param name="id">Schema id.</param>
    /// <param name="includeDeleted">When true, soft-deleted schemas can be returned.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The schema, or <c>null</c> if no match exists.</returns>
    Task<Schema?> GetByIdAsync(Guid id, bool includeDeleted, CancellationToken ct = default);

    /// <summary>Persist a brand-new schema.</summary>
    /// <param name="input">A populated <see cref="Schema"/> (the id is overwritten).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted schema.</returns>
    /// <exception cref="ConflictException">A schema with the same name already exists (including soft-deleted entries).</exception>
    /// <exception cref="ValidationException">
    /// The schema failed structural validation (layout references a missing value, duplicate value
    /// reference, section without caption, layout nested past the safety cap, <c>Version</c>
    /// negative, or some value's <c>SinceVersion</c> is outside <c>[0, Version]</c>).
    /// </exception>
    Task<Schema> CreateAsync(Schema input, CancellationToken ct = default);

    /// <summary>Replace every overwritable field on an existing schema.</summary>
    /// <remarks>
    /// <see cref="Schema.Version"/> is monotonic: callers may keep it the same or increase it but
    /// never lower it. Whenever <see cref="Schema.Version"/> actually changes, the service stamps
    /// <see cref="Schema.VersionModifiedAt"/> with the current UTC timestamp; otherwise the field
    /// is preserved untouched. Incoming <see cref="Schema.VersionModifiedAt"/> values are always
    /// ignored.
    /// </remarks>
    /// <param name="id">Schema id.</param>
    /// <param name="input">The new values; immutable fields on <paramref name="input"/> are ignored.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated schema, or <c>null</c> if no schema with that id exists.</returns>
    /// <exception cref="ConflictException">The new name collides with another schema.</exception>
    /// <exception cref="ValidationException">
    /// Same conditions as <see cref="CreateAsync"/>, plus: <see cref="Schema.Version"/> would
    /// decrease relative to the persisted value.
    /// </exception>
    Task<Schema?> UpdateAsync(Guid id, Schema input, CancellationToken ct = default);

    /// <summary>Soft-delete a schema. Idempotent.</summary>
    /// <param name="id">Schema id.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Make an independent copy of an existing schema, picking a unique name by appending
    /// <c>_copy</c> (then <c>_copy_2</c>, <c>_copy_3</c>, …) until no collision is found.
    /// </summary>
    /// <remarks>
    /// The clone keeps every overwritable field (<c>Values</c>, <c>Layout</c>, <c>Version</c>,
    /// <c>SubmissionValidations</c>, <c>Audience</c>, <c>Modifiable</c>, <c>Enabled</c>, …). Audit
    /// fields and the id are reset; <see cref="Schema.VersionModifiedAt"/> is stamped with the
    /// current UTC timestamp so the clone behaves like a brand-new schema for the New-tag rule.
    /// </remarks>
    /// <param name="id">Source schema id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted clone, or <c>null</c> if no schema with that id exists.</returns>
    Task<Schema?> CloneAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Generate a flat <see cref="SubmissionInput"/> example for a schema, picking default values
    /// per <see cref="SchemaValueType"/>: empty string for <c>String</c>, <c>0</c> (or <c>Min</c>)
    /// for numerics, <c>today</c> (or <c>MinDate</c>) for <c>Date</c>, <c>false</c> for
    /// <c>Boolean</c>. Validation rules are intentionally ignored — the goal is to give callers a
    /// starting template, not a guaranteed-valid submission.
    /// </summary>
    /// <param name="serviceAccountId">Calling account; used to apply the visibility rule.</param>
    /// <param name="name">Machine-style schema name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The example payload, or <c>null</c> if the schema does not exist or is not visible to the caller.</returns>
    Task<SubmissionInput?> BuildExampleSubmissionAsync(Guid serviceAccountId, string name, CancellationToken ct = default);

    /// <summary>Aggregate all submissions for a schema into per-value/per-bucket statistics.</summary>
    /// <param name="name">Machine-style schema name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The aggregated history, or <c>null</c> if no schema with that name exists. A schema with no
    /// submitted samples returns a non-null result with empty bucket lists.
    /// </returns>
    Task<SchemaHistory?> GetHistoryAsync(string name, CancellationToken ct = default);

    /// <summary>Page through a schema's saved version snapshots, newest change first.</summary>
    /// <param name="name">Machine-style schema name.</param>
    /// <param name="request">Paging parameters.</param>
    /// <param name="from">Lower bound on the change date (inclusive) when set.</param>
    /// <param name="to">Upper bound on the change date (exclusive) when set.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A page of snapshots with the total count.</returns>
    Task<PagedResult<SchemaVersionHistory>> GetVersionHistoryAsync(
        string name,
        PageRequest request,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default);

    /// <summary>Fetch a single version snapshot by id (the full schema at that point in time).</summary>
    /// <param name="entryId">Snapshot id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The snapshot, or <c>null</c> if it doesn't exist.</returns>
    Task<SchemaVersionHistory?> GetVersionSnapshotAsync(Guid entryId, CancellationToken ct = default);

    /// <summary>
    /// Permanently delete a single version-history entry. Records an audit-log Delete against the
    /// schema. Never affects the live schema.
    /// </summary>
    /// <param name="name">Machine-style schema name the entry belongs to.</param>
    /// <param name="entryId">Snapshot id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> when an entry was removed; <c>false</c> when none matched.</returns>
    Task<bool> DeleteVersionEntryAsync(string name, Guid entryId, CancellationToken ct = default);

    /// <summary>
    /// Permanently delete the entire version history for a schema. Records an audit-log Delete
    /// against the schema. Never affects the live schema.
    /// </summary>
    /// <param name="name">Machine-style schema name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of entries removed.</returns>
    Task<long> DeleteVersionHistoryAsync(string name, CancellationToken ct = default);
}
