using Ingest.Api.Models;
using Ingest.Core.Abstractions;

namespace Ingest.Api.Common;

/// <summary>Internal mapper between the service-layer status tree and the API DTOs.</summary>
internal static class StatusMapper
{
    /// <summary>Project the domain ServiceStatus tree into the wire-format DTO.</summary>
    /// <param name="s">Source status snapshot.</param>
    /// <returns>The wire representation.</returns>
    public static ServiceStatusDto ToDto(ServiceStatus s) => new(
        s.ServiceId,
        s.ServiceName,
        s.Period,
        s.Schemas.Select(sc => new SchemaStatusDto(
            sc.SchemaName, sc.Label, sc.Enabled,
            sc.Values.Select(v => new SchemaValueStatusDto(
                v.ValueName, v.Label, v.Cadence, v.Required, v.Enabled,
                v.PeriodStart, v.PeriodEnd,
                v.LastSubmissionId, v.LastTimestamp, v.Satisfied)).ToList())).ToList());

    /// <summary>Project the domain missing-submissions report into the wire-format DTO list.</summary>
    /// <param name="report">Source per-cadence missing report.</param>
    /// <returns>The wire representation, preserving the source ordering.</returns>
    public static List<MissingByCadenceDto> ToDto(IReadOnlyList<MissingByCadence> report) =>
        report.Select(b => new MissingByCadenceDto(
            b.Cadence,
            b.PeriodStart,
            b.PeriodEnd,
            b.Entries.Select(e => new MissingSubmissionEntryDto(
                e.ServiceId,
                e.ServiceName,
                e.ServiceLabel,
                e.SchemaName,
                e.SchemaLabel,
                e.MissingRequiredCount,
                e.TotalRequiredCount)).ToList())).ToList();
}
