using Ingest.Core.Entities;

namespace Ingest.Core.Abstractions;

/// <summary>
/// The aggregation a single <see cref="IExploreService"/> bucket reduces its samples to. Picked
/// by the caller; the same query shape supports every option so the UI can switch without a
/// round-trip change.
/// </summary>
public enum ExploreAggregation
{
    /// <summary>Arithmetic mean of the samples in the bucket.</summary>
    Average = 0,

    /// <summary>Sum of the samples in the bucket.</summary>
    Sum = 1,

    /// <summary>Smallest sample in the bucket.</summary>
    Min = 2,

    /// <summary>Largest sample in the bucket.</summary>
    Max = 3,

    /// <summary>Number of samples in the bucket (the reduced value equals the count).</summary>
    Count = 4,
}

/// <summary>Filter + shaping options for an <see cref="IExploreService.GetSeriesAsync"/> call.</summary>
/// <param name="SchemaName">Machine-style schema name to explore. Required.</param>
/// <param name="ValueNames">
/// Restrict to these value names. <c>null</c> or empty means "every numeric value on the schema".
/// Non-numeric and unknown names are ignored.
/// </param>
/// <param name="ServiceIds">Restrict to samples from these services. <c>null</c> or empty means "every service".</param>
/// <param name="From">Inclusive lower bound on the sample timestamp.</param>
/// <param name="To">Exclusive upper bound on the sample timestamp.</param>
/// <param name="Aggregation">How each cadence bucket reduces its samples.</param>
public sealed record ExploreSeriesQuery(
    string SchemaName,
    IReadOnlyList<string>? ValueNames,
    IReadOnlyList<Guid>? ServiceIds,
    DateTime? From,
    DateTime? To,
    ExploreAggregation Aggregation);

/// <summary>A service that appears in an explore result, with its friendly label resolved.</summary>
/// <param name="ServiceId">Service account id.</param>
/// <param name="ServiceName">Machine-style service name (snapshot from the samples).</param>
/// <param name="ServiceLabel">Friendly label, or <c>null</c> when none is set.</param>
public sealed record ExploreServiceRef(Guid ServiceId, string ServiceName, string? ServiceLabel);

/// <summary>One service's reduced value inside a single cadence bucket.</summary>
/// <param name="ServiceId">Service account id (join key back to <see cref="ExploreServiceRef"/>).</param>
/// <param name="Value">The bucket reduced by the query's <see cref="ExploreAggregation"/>.</param>
/// <param name="Count">Number of samples this service contributed to the bucket.</param>
public sealed record ExploreServicePoint(Guid ServiceId, double Value, int Count);

/// <summary>One cadence bucket of a value's timeline, carrying the overall and per-service reductions.</summary>
/// <param name="PeriodStart">Inclusive bucket start, aligned to the value's cadence.</param>
/// <param name="PeriodEnd">Exclusive bucket end.</param>
/// <param name="Value">The bucket reduced across every in-scope service.</param>
/// <param name="Count">Total samples folded into the bucket across every service.</param>
/// <param name="Services">Per-service reductions, ordered by service name.</param>
public sealed record ExploreBucket(
    DateTime PeriodStart,
    DateTime PeriodEnd,
    double Value,
    int Count,
    IReadOnlyList<ExploreServicePoint> Services);

/// <summary>A single value's bucketed timeline.</summary>
/// <param name="ValueName">Machine-style value name.</param>
/// <param name="Label">Friendly label, if one was set on the schema.</param>
/// <param name="Type">Value type (always numeric — non-numeric values never produce a series).</param>
/// <param name="Cadence">Cadence the buckets follow.</param>
/// <param name="Unit">Unit of measure carried on the schema definition.</param>
/// <param name="Buckets">Buckets ordered chronologically.</param>
public sealed record ExploreValueSeries(
    string ValueName,
    string? Label,
    SchemaValueType Type,
    Cadence Cadence,
    string? Unit,
    IReadOnlyList<ExploreBucket> Buckets);

/// <summary>The result of an <see cref="IExploreService.GetSeriesAsync"/> call.</summary>
/// <param name="SchemaName">Schema that was explored.</param>
/// <param name="SchemaLabel">Friendly schema label.</param>
/// <param name="Aggregation">The aggregation applied to every bucket.</param>
/// <param name="From">Resolved lower bound echoed back from the request.</param>
/// <param name="To">Resolved upper bound echoed back from the request.</param>
/// <param name="Services">Every service appearing in the result, with labels resolved.</param>
/// <param name="Values">One timeline per in-scope numeric value.</param>
public sealed record ExploreSeriesResult(
    string SchemaName,
    string? SchemaLabel,
    ExploreAggregation Aggregation,
    DateTime? From,
    DateTime? To,
    IReadOnlyList<ExploreServiceRef> Services,
    IReadOnlyList<ExploreValueSeries> Values);

/// <summary>How the scorecard picks which sample represents each service.</summary>
public enum ScorecardMode
{
    /// <summary>Each service's most recent submission for the value, however old. Services that never reported are omitted.</summary>
    LatestAvailable = 0,

    /// <summary>
    /// Only one specific period (see <see cref="ScorecardPeriod"/>). Every service that has ever
    /// reported the value is shown; one that didn't submit that period gets a "missing" cell.
    /// </summary>
    LastPeriod = 1,
}

/// <summary>Which period <see cref="ScorecardMode.LastPeriod"/> looks at, relative to now.</summary>
public enum ScorecardPeriod
{
    /// <summary>The period that contains "now", even though it is still open.</summary>
    Current = 0,

    /// <summary>The most recent fully-elapsed period (the one before the current).</summary>
    LatestClosed = 1,
}

/// <summary>Filter options for an <see cref="IExploreService.GetScorecardAsync"/> call.</summary>
/// <param name="ServiceIds">Restrict to these services. <c>null</c> or empty means "every service".</param>
/// <param name="Mode">Whether to show each service's latest sample or a single period.</param>
/// <param name="Period">Which period to read when <paramref name="Mode"/> is <see cref="ScorecardMode.LastPeriod"/>.</param>
public sealed record ExploreScorecardQuery(
    IReadOnlyList<Guid>? ServiceIds,
    ScorecardMode Mode = ScorecardMode.LatestAvailable,
    ScorecardPeriod Period = ScorecardPeriod.Current);

/// <summary>
/// One service's sample for a banded value, with its RAG classification. A "missing" cell (the
/// service didn't submit the requested period) carries a <c>null</c> <see cref="Status"/>,
/// <see cref="Value"/> and <see cref="SubmissionId"/>, with the period it was expected for.
/// </summary>
/// <param name="ServiceId">Service account id (join key back to <see cref="ExploreScorecardResult.Services"/>).</param>
/// <param name="SubmissionId">Submission the sample came from, so the UI can deep-link to it; <c>null</c> when missing.</param>
/// <param name="Value">The numeric value the service reported; <c>null</c> when missing.</param>
/// <param name="Status">Where <paramref name="Value"/> falls in the value's target band; <c>null</c> when missing.</param>
/// <param name="PeriodStart">Inclusive start of the period the sample belongs to (or was expected for).</param>
/// <param name="PeriodEnd">Exclusive end of that period.</param>
/// <param name="SubmittedAt">When the submission carrying the sample was accepted; <c>null</c> when missing.</param>
public sealed record ExploreScorecardCell(
    Guid ServiceId,
    Guid? SubmissionId,
    double? Value,
    RagStatus? Status,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    DateTime? SubmittedAt);

/// <summary>A single banded value and the latest RAG status of every service that reported it.</summary>
/// <param name="ValueName">Machine-style value name.</param>
/// <param name="Label">Friendly label, if one was set.</param>
/// <param name="Unit">Unit of measure carried on the schema definition.</param>
/// <param name="Cadence">Cadence the value is collected on (snapshot from the schema definition).</param>
/// <param name="AmberMin">Lower edge of the acceptable (amber) range, or <c>null</c>.</param>
/// <param name="GreenMin">Lower edge of the ideal (green) range, or <c>null</c>.</param>
/// <param name="GreenMax">Upper edge of the ideal (green) range, or <c>null</c>.</param>
/// <param name="AmberMax">Upper edge of the acceptable (amber) range, or <c>null</c>.</param>
/// <param name="Cells">One cell per service that has a latest sample, ordered by service id.</param>
public sealed record ExploreScorecardValue(
    string ValueName,
    string? Label,
    string? Unit,
    Cadence Cadence,
    double? AmberMin,
    double? GreenMin,
    double? GreenMax,
    double? AmberMax,
    IReadOnlyList<ExploreScorecardCell> Cells);

/// <summary>One enabled schema's banded values for the scorecard, grouped under the schema.</summary>
/// <param name="SchemaName">Machine-style schema name.</param>
/// <param name="SchemaLabel">Friendly schema label.</param>
/// <param name="Values">Banded numeric values that have at least one reporting service.</param>
public sealed record ExploreScorecardSchema(
    string SchemaName,
    string? SchemaLabel,
    IReadOnlyList<ExploreScorecardValue> Values);

/// <summary>The result of an <see cref="IExploreService.GetScorecardAsync"/> call.</summary>
/// <param name="Services">Every service appearing in the result, with labels resolved.</param>
/// <param name="Schemas">Enabled schemas that have at least one banded value with data.</param>
public sealed record ExploreScorecardResult(
    IReadOnlyList<ExploreServiceRef> Services,
    IReadOnlyList<ExploreScorecardSchema> Schemas);

/// <summary>
/// Lightweight, in-app analytics over the denormalised sample projection: per-value, per-cadence
/// buckets with a per-service breakdown, for the bundled "Explore" page. Deliberately small — it
/// is a budget convenience for deployments without Power BI/Excel, not a BI engine. Serious
/// analysis still belongs in a BI tool against the OData feed.
/// </summary>
public interface IExploreService
{
    /// <summary>Build a bucketed, per-service series for one schema.</summary>
    /// <param name="query">Filter + aggregation options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The series, or <c>null</c> when no schema with that name exists. A schema with no matching
    /// numeric values (or no samples) returns a non-null result with empty <see cref="ExploreSeriesResult.Values"/>.
    /// </returns>
    Task<ExploreSeriesResult?> GetSeriesAsync(ExploreSeriesQuery query, CancellationToken ct = default);

    /// <summary>
    /// Build a cross-schema RAG scorecard: every enabled schema's numeric values that carry a
    /// target band, with each reporting service's latest sample classified green/amber/red. Powers
    /// the Explore page's at-a-glance status board. Schemas and values with no banded data are
    /// omitted entirely.
    /// </summary>
    /// <param name="query">Service filter.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ExploreScorecardResult> GetScorecardAsync(ExploreScorecardQuery query, CancellationToken ct = default);
}
