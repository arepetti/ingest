using Ingest.Core.Entities;
using Ingest.Core.Validation;

namespace Ingest.Infrastructure.Services;

/// <summary>
/// Pure mapper that flattens a <see cref="Submission"/> into the denormalised one-row-per-sample
/// <see cref="SampleProjection"/> entries consumed by the OData feed and the admin query
/// endpoint. Each projection row carries a snapshot of the relevant schema metadata so the
/// reporting layer doesn't have to join.
/// </summary>
public static class SampleProjectionBuilder
{
    /// <summary>Build the projection rows for a saved submission.</summary>
    /// <param name="submission">The submission to project.</param>
    /// <param name="schemasByName">
    /// Schemas indexed by their <c>Name</c>. Samples referring to a schema or value that isn't in
    /// the dictionary are silently skipped — they will already have been rejected by the
    /// validator so reaching this code path indicates a broken state and we'd rather degrade than
    /// crash the projection rebuild.
    /// </param>
    /// <returns>One row per (resolvable) sample, in submission order.</returns>
    public static IEnumerable<SampleProjection> Build(Submission submission, IReadOnlyDictionary<string, Schema> schemasByName)
    {
        foreach (var s in submission.Samples)
        {
            if (!schemasByName.TryGetValue(s.SchemaName, out var schema)) continue;
            var def = schema.Values.FirstOrDefault(v =>
                string.Equals(v.Name, s.ValueName, StringComparison.OrdinalIgnoreCase));
            if (def is null) continue;

            var (start, end) = CadenceCalculator.BucketFor(def.Cadence, s.Timestamp);

            var p = new SampleProjection
            {
                SubmissionId = submission.Id,
                ServiceAccountId = submission.ServiceAccountId,
                ServiceName = submission.ServiceName ?? string.Empty,
                SchemaName = s.SchemaName,
                ValueName = s.ValueName,
                ValueType = def.Type,
                Timestamp = DateTime.SpecifyKind(s.Timestamp, DateTimeKind.Utc),
                Note = s.Note,
                Cadence = def.Cadence,
                PeriodStart = start,
                PeriodEnd = end,
            };

            switch (def.Type)
            {
                case SchemaValueType.String: p.StringValue = s.Value as string; break;
                case SchemaValueType.Integer: p.IntegerValue = ToLong(s.Value); break;
                case SchemaValueType.Number: p.NumberValue = ToDouble(s.Value); break;
                case SchemaValueType.Date: p.DateValue = ToDate(s.Value); break;
                case SchemaValueType.Boolean: p.BooleanValue = s.Value as bool?; break;
            }

            yield return p;
        }
    }

    private static long? ToLong(object? v) => v switch
    {
        long l => l,
        int i => i,
        double d => (long)d,
        decimal m => (long)m,
        string s when long.TryParse(s, out var p) => p,
        _ => null,
    };

    private static double? ToDouble(object? v) => v switch
    {
        double d => d,
        float f => f,
        int i => i,
        long l => l,
        decimal m => (double)m,
        string s when double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var p) => p,
        _ => null,
    };

    private static DateTime? ToDate(object? v) => v switch
    {
        DateTime dt => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
        DateTimeOffset dto => dto.UtcDateTime,
        string s when DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var p) => p,
        _ => null,
    };
}
