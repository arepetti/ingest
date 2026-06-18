using Ingest.Core.Common;
using Ingest.Core.Entities;

namespace Ingest.Core.Abstractions;

/// <summary>Submission status for a single schema value within the current cadence window.</summary>
/// <param name="ValueName">Machine-style name of the schema value.</param>
/// <param name="Label">Optional friendly label from the schema.</param>
/// <param name="Cadence">Reporting cadence declared on the schema value.</param>
/// <param name="Required">Whether the value is marked required on its schema.</param>
/// <param name="Enabled">Whether the value (and its parent schema) is currently enabled. Disabled entries are still reported so callers can render a complete UI.</param>
/// <param name="PeriodStart">Inclusive start of the current cadence bucket.</param>
/// <param name="PeriodEnd">Exclusive end of the current cadence bucket.</param>
/// <param name="LastSubmissionId">Id of the most recent submission carrying this value within the bucket, if any.</param>
/// <param name="LastTimestamp">Timestamp of the most recent qualifying submission, if any.</param>
/// <param name="Satisfied">True when the bucket contains at least one submission of this value (or the value is optional).</param>
public sealed record SchemaValueStatus(
    string ValueName,
    string? Label,
    Cadence Cadence,
    bool Required,
    bool Enabled,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    Guid? LastSubmissionId,
    DateTime? LastTimestamp,
    bool Satisfied);

/// <summary>Submission status for a single schema (all of its values rolled up).</summary>
/// <param name="SchemaName">Machine-style schema name.</param>
/// <param name="Label">Optional friendly label.</param>
/// <param name="Enabled">Schema-level enabled flag.</param>
/// <param name="Values">Per-value statuses; disabled entries are included so callers can filter them out.</param>
public sealed record SchemaStatus(
    string SchemaName,
    string? Label,
    bool Enabled,
    IReadOnlyList<SchemaValueStatus> Values);

/// <summary>Submission status for a single service across all schemas it is allowed to submit against.</summary>
/// <param name="ServiceId">Account id of the inspected service.</param>
/// <param name="ServiceName">Machine-style account name.</param>
/// <param name="Period">Period hint used to render dashboard headers; per-value satisfaction is always computed against the value's own cadence.</param>
/// <param name="Schemas">Per-schema statuses.</param>
public sealed record ServiceStatus(
    Guid ServiceId,
    string ServiceName,
    string Period,
    IReadOnlyList<SchemaStatus> Schemas);

/// <summary>
/// One row of the "missing submissions" report: a service that hasn't submitted every required
/// value of a given cadence for one of its visible schemas inside the current cadence window.
/// </summary>
/// <param name="ServiceId">Owning service account id.</param>
/// <param name="ServiceName">Service account name.</param>
/// <param name="ServiceLabel">Service account label (display).</param>
/// <param name="SchemaName">Schema name the missing values belong to.</param>
/// <param name="SchemaLabel">Schema label (display).</param>
/// <param name="MissingRequiredCount">Number of required-and-enabled values of this cadence on the schema that haven't been submitted in the current bucket.</param>
/// <param name="TotalRequiredCount">Total number of required-and-enabled values of this cadence on the schema (the denominator).</param>
public sealed record MissingSubmissionEntry(
    Guid ServiceId,
    string ServiceName,
    string? ServiceLabel,
    string SchemaName,
    string? SchemaLabel,
    int MissingRequiredCount,
    int TotalRequiredCount);

/// <summary>
/// Which cadence window a <see cref="MissingByCadence"/> bucket describes. The dashboard renders
/// <see cref="Current"/> buckets as a soft warning (the window is still open) and
/// <see cref="Previous"/> buckets as overdue (the window has closed).
/// </summary>
public enum MissingPeriodKind
{
    /// <summary>The bucket covers the cadence window containing "now" — submissions can still arrive.</summary>
    Current = 0,

    /// <summary>The bucket covers the cadence window immediately before the current one — its deadline has passed.</summary>
    Previous = 1,
}

/// <summary>
/// "Missing submissions" report bucketed by cadence. The bucket's <see cref="PeriodStart"/> and
/// <see cref="PeriodEnd"/> are the cadence window (current or previous, per <see cref="Period"/>);
/// <see cref="Entries"/> holds every (service, schema) tuple that's short at least one
/// required-and-enabled value of that cadence inside the window.
/// </summary>
/// <param name="Cadence">Cadence the bucket covers.</param>
/// <param name="PeriodStart">Inclusive start of the cadence window.</param>
/// <param name="PeriodEnd">Exclusive end of the cadence window.</param>
/// <param name="Period">Whether the window is the current (still-open) one or the previous (overdue) one.</param>
/// <param name="Entries">One row per (service, schema) tuple with missing required values. Sorted by service label then schema label.</param>
public sealed record MissingByCadence(
    Cadence Cadence,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    MissingPeriodKind Period,
    IReadOnlyList<MissingSubmissionEntry> Entries);

/// <summary>
/// Detailed missing-submissions report for a single cadence and a single (possibly historical)
/// window. Mirrors one <see cref="MissingByCadence"/> bucket but is addressed by an explicit
/// <see cref="Offset"/> (0 = current, -1 = previous, -N = N periods ago) so the analytics page
/// can page back through time.
/// </summary>
/// <param name="Cadence">Cadence the window belongs to.</param>
/// <param name="Offset">Signed bucket offset from "now" (0 = current, negative = past).</param>
/// <param name="PeriodStart">Inclusive start of the window.</param>
/// <param name="PeriodEnd">Exclusive end of the window.</param>
/// <param name="Entries">One row per (service, schema) tuple short at least one required value in the window.</param>
public sealed record MissingPeriodReport(
    Cadence Cadence,
    int Offset,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    IReadOnlyList<MissingSubmissionEntry> Entries);

/// <summary>One point on the "missing submissions over time" trend for a single cadence.</summary>
/// <param name="Offset">Signed bucket offset from "now" (0 = current, negative = past).</param>
/// <param name="PeriodStart">Inclusive start of the window.</param>
/// <param name="PeriodEnd">Exclusive end of the window.</param>
/// <param name="TotalMissing">Total number of missing required values across every service and schema in the window.</param>
public sealed record MissingHistoryPoint(
    int Offset,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    int TotalMissing);

/// <summary>The "missing submissions over time" trend for a single cadence, oldest period first.</summary>
/// <param name="Cadence">Cadence the trend covers.</param>
/// <param name="Points">One point per period, ordered oldest → current.</param>
public sealed record MissingHistory(
    Cadence Cadence,
    IReadOnlyList<MissingHistoryPoint> Points);

/// <summary>
/// Builds the per-value <em>cadence freshness</em> snapshot used by both the <c>/api/me/status</c>
/// endpoint and the operator-facing <c>/api/services/{name}/status</c>. The snapshot is the only
/// way for a service to confirm at a glance that all of its scheduled values have been submitted
/// inside their current windows.
/// </summary>
public interface IStatusService
{
    /// <summary>Compute the freshness snapshot for a service identified by id.</summary>
    /// <param name="serviceId">Account id of the service to inspect.</param>
    /// <param name="period">Period hint forwarded to the response.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The status for every schema the service is allowed to submit against.</returns>
    Task<ServiceStatus> GetStatusAsync(Guid serviceId, string period, CancellationToken ct = default);

    /// <summary>Like <see cref="GetStatusAsync"/> but resolves the service by its unique machine-style name first.</summary>
    /// <param name="serviceName">Account name.</param>
    /// <param name="period">Period hint forwarded to the response.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The status for every schema the service is allowed to submit against.</returns>
    /// <exception cref="NotFoundException">No account with that name exists.</exception>
    Task<ServiceStatus> GetStatusByServiceNameAsync(string serviceName, string period, CancellationToken ct = default);

    /// <summary>
    /// Aggregate every Service-role account's submission status into a per-cadence "what's
    /// missing" report covering both the current cadence window and the previous one. The result
    /// includes only (period, cadence) combinations that have at least one (service, schema)
    /// tuple with unsatisfied required values — combinations with nothing missing are omitted, so
    /// the caller can render one card per window that actually warrants attention. Current-window
    /// buckets come first (ordered by cadence), then previous-window buckets.
    /// </summary>
    /// <remarks>
    /// Disabled accounts, disabled schemas, and disabled values are skipped — they cannot be
    /// satisfied by definition, and including them would surface noise. Optional values are
    /// also skipped (the report is specifically about <em>required</em> drift). Previous-window
    /// entries are only produced for schemas <em>and</em> services that already existed before
    /// the window closed (<c>CreatedAt &lt; PeriodEnd</c>), so freshly-onboarded services and
    /// brand-new schemas aren't retroactively flagged as overdue. The walk is
    /// O(services × schemas × required values); fine for the working-set of a council-sized
    /// registry, would want pre-aggregation in Mongo for anything larger.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current-window buckets (ordered by cadence) followed by previous-window buckets, each with at least one missing entry.</returns>
    Task<IReadOnlyList<MissingByCadence>> GetMissingAsync(CancellationToken ct = default);

    /// <summary>
    /// Detailed missing-submissions report for a single cadence and a single window addressed by
    /// <paramref name="offset"/> (0 = current, -1 = previous, -N = N periods ago). Powers the
    /// per-period analytics page's table and per-service breakdown.
    /// </summary>
    /// <remarks>
    /// Same skip rules as <see cref="GetMissingAsync"/>; the <c>CreatedAt &lt; PeriodEnd</c> guard
    /// applies to every window (a schema/service that didn't exist yet can't owe data).
    /// </remarks>
    /// <param name="cadence">Cadence to evaluate.</param>
    /// <param name="offset">Signed bucket offset from "now" (0 = current, negative = past).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The window bounds and one entry per (service, schema) tuple short at least one required value.</returns>
    Task<MissingPeriodReport> GetMissingForPeriodAsync(Cadence cadence, int offset, CancellationToken ct = default);

    /// <summary>
    /// Build the "missing submissions over time" trend for a single cadence: the total count of
    /// missing required values for each of the last <paramref name="periods"/> windows, oldest
    /// first and ending with the current window. Powers the analytics page's trend chart.
    /// </summary>
    /// <param name="cadence">Cadence to evaluate.</param>
    /// <param name="periods">Number of windows to include (clamped to a sane range).</param>
    /// <param name="serviceId">Optional service to scope the trend to. When null the trend covers every service (the global view).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One point per window, ordered oldest → current.</returns>
    Task<MissingHistory> GetMissingHistoryAsync(Cadence cadence, int periods, Guid? serviceId = null, CancellationToken ct = default);
}
