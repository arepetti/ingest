using Ingest.Core.Abstractions;
using Ingest.Core.Entities;

namespace Ingest.Api.Odata;

/// <summary>
/// Flat, one-row-per-(schema, value, service) projection of the cross-schema RAG scorecard,
/// shaped for OData/PowerBI. Unlike the nested admin <c>ExploreScorecardResponse</c>, every row
/// is fully denormalised (schema/value/service labels, the target band edges and the RAG status
/// as text) so a BI tool can pivot it without any joins. Served by the unbound
/// <c>scorecard(mode,period)</c> function — see <see cref="ScorecardController"/>.
/// </summary>
public sealed class ScorecardCard
{
    /// <summary>
    /// Stable synthetic key: <c>schema|value|service|periodStart</c>. Deterministic so PowerBI
    /// incremental refresh and row de-duplication behave, and so OData has a key for the entity.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>Machine-style schema name.</summary>
    public required string SchemaName { get; set; }

    /// <summary>Friendly schema label, or <c>null</c>.</summary>
    public string? SchemaLabel { get; set; }

    /// <summary>Machine-style value name.</summary>
    public required string ValueName { get; set; }

    /// <summary>Friendly value label, or <c>null</c>.</summary>
    public string? ValueLabel { get; set; }

    /// <summary>Unit of measure carried on the schema definition, or <c>null</c>.</summary>
    public string? Unit { get; set; }

    /// <summary>Cadence the value is collected on.</summary>
    public Cadence Cadence { get; set; }

    /// <summary>Service account id.</summary>
    public Guid ServiceId { get; set; }

    /// <summary>Machine-style service name snapshot.</summary>
    public required string ServiceName { get; set; }

    /// <summary>Friendly service label, or <c>null</c>.</summary>
    public string? ServiceLabel { get; set; }

    /// <summary>Inclusive start of the period the cell belongs to (or was expected for).</summary>
    public DateTime PeriodStart { get; set; }

    /// <summary>Exclusive end of that period.</summary>
    public DateTime PeriodEnd { get; set; }

    /// <summary>The numeric value the service reported; <c>null</c> when missing.</summary>
    public double? Value { get; set; }

    /// <summary>
    /// RAG classification as text: <c>Green</c>, <c>Amber</c>, <c>Red</c>, or <c>Missing</c> when
    /// the service did not report the requested period. Always populated (never null) so PowerBI
    /// can slice on it directly.
    /// </summary>
    public required string Status { get; set; }

    /// <summary>Submission the sample came from; <c>null</c> when missing.</summary>
    public Guid? SubmissionId { get; set; }

    /// <summary>When the submission was accepted by the API; <c>null</c> when missing.</summary>
    public DateTime? SubmittedAt { get; set; }

    /// <summary>Lower edge of the acceptable (amber) range, or <c>null</c>.</summary>
    public double? AmberMin { get; set; }

    /// <summary>Lower edge of the ideal (green) range, or <c>null</c>.</summary>
    public double? GreenMin { get; set; }

    /// <summary>Upper edge of the ideal (green) range, or <c>null</c>.</summary>
    public double? GreenMax { get; set; }

    /// <summary>Upper edge of the acceptable (amber) range, or <c>null</c>.</summary>
    public double? AmberMax { get; set; }

    /// <summary>Status text used for a "missing" cell (service didn't report the requested period).</summary>
    public const string MissingStatus = "Missing";

    /// <summary>
    /// Flatten a nested <see cref="ExploreScorecardResult"/> into one card per cell. Service labels
    /// are resolved from <see cref="ExploreScorecardResult.Services"/>; an unmatched service falls
    /// back to its id as the name. Ordering follows the result (schema → value → service).
    /// </summary>
    /// <param name="result">The scorecard produced by <see cref="IExploreService.GetScorecardAsync"/>.</param>
    /// <returns>The flat cards, ready to be exposed over OData.</returns>
    public static IEnumerable<ScorecardCard> FromResult(ExploreScorecardResult result)
    {
        var serviceById = result.Services.ToDictionary(s => s.ServiceId);

        foreach (var schema in result.Schemas)
        foreach (var value in schema.Values)
        foreach (var cell in value.Cells)
        {
            serviceById.TryGetValue(cell.ServiceId, out var svc);
            yield return new ScorecardCard
            {
                Id = $"{schema.SchemaName}|{value.ValueName}|{cell.ServiceId:N}|{cell.PeriodStart:O}",
                SchemaName = schema.SchemaName,
                SchemaLabel = schema.SchemaLabel,
                ValueName = value.ValueName,
                ValueLabel = value.Label,
                Unit = value.Unit,
                Cadence = value.Cadence,
                ServiceId = cell.ServiceId,
                ServiceName = svc?.ServiceName ?? cell.ServiceId.ToString(),
                ServiceLabel = svc?.ServiceLabel,
                PeriodStart = cell.PeriodStart,
                PeriodEnd = cell.PeriodEnd,
                Value = cell.Value,
                Status = cell.Status?.ToString() ?? MissingStatus,
                SubmissionId = cell.SubmissionId,
                SubmittedAt = cell.SubmittedAt,
                AmberMin = value.AmberMin,
                GreenMin = value.GreenMin,
                GreenMax = value.GreenMax,
                AmberMax = value.AmberMax,
            };
        }
    }
}
