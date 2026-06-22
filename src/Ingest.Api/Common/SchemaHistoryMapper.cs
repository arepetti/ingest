using Ingest.Api.Models;
using Ingest.Core.Abstractions;

namespace Ingest.Api.Common;

/// <summary>Internal mapper between the service-layer schema-history aggregate and the API DTOs.</summary>
internal static class SchemaHistoryMapper
{
    /// <summary>Project the service-layer history tree into the wire-format DTO.</summary>
    /// <param name="h">Source history aggregate.</param>
    /// <returns>The wire representation.</returns>
    public static SchemaHistoryDto ToDto(SchemaHistory h) => new(
        h.SchemaName,
        h.Label,
        h.Values.Select(v => new SchemaValueHistoryDto(
            v.ValueName, v.Label, v.Type, v.Cadence, v.Unit, v.GreenMin, v.GreenMax, v.AmberMin, v.AmberMax,
            v.Buckets.Select(b => new HistoryBucketDto(
                b.PeriodStart, b.PeriodEnd, b.Min, b.Max, b.Average, b.Count)).ToList()))
        .ToList());
}
