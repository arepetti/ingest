using Ingest.Core.Abstractions;
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
    /// <param name="evaluator">
    /// When supplied, calculated schema values are evaluated and emitted as derived projection
    /// rows (<see cref="SampleProjection.IsDerived"/> = <c>true</c>). When <c>null</c>, derived
    /// rows are skipped (useful in unit tests that only care about submitted samples).
    /// </param>
    /// <param name="anchors">Cadence bucket alignment to use; <c>null</c> = the historical calendar defaults.</param>
    /// <returns>One row per (resolvable) sample, in submission order, plus derived rows per schema.</returns>
    public static IEnumerable<SampleProjection> Build(
        Submission submission,
        IReadOnlyDictionary<string, Schema> schemasByName,
        IExpressionEvaluator? evaluator = null,
        CadenceAnchors? anchors = null)
    {
        foreach (var s in submission.Samples)
        {
            if (!schemasByName.TryGetValue(s.SchemaName, out var schema)) continue;
            var def = schema.Values.FirstOrDefault(v =>
                string.Equals(v.Name, s.ValueName, StringComparison.OrdinalIgnoreCase));
            if (def is null || def.IsCalculated) continue;

            yield return ToProjection(submission, s, def, anchors);
        }

        if (evaluator is null) yield break;

        var schemaNames = submission.Samples
            .Select(s => s.SchemaName)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var schemaName in schemaNames)
        {
            if (!schemasByName.TryGetValue(schemaName, out var schema)) continue;

            var schemaSamples = submission.Samples
                .Where(s => string.Equals(s.SchemaName, schemaName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (schemaSamples.Count == 0) continue;

            var submitted = schemaSamples
                .Where(s => schema.Values.FirstOrDefault(v =>
                    string.Equals(v.Name, s.ValueName, StringComparison.OrdinalIgnoreCase)) is { IsCalculated: false })
                .GroupBy(s => s.ValueName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => (object?)g.First().Value, StringComparer.OrdinalIgnoreCase);

            var context = DerivedValueCalculator.Compute(schema, submitted, evaluator);
            var timestamp = schemaSamples.Max(s => s.Timestamp);

            foreach (var def in schema.Values.Where(v => v.IsCalculated))
            {
                if (!context.TryGetValue(def.Name, out var value) || value is null) continue;
                yield return ToDerivedProjection(submission, schemaName, def, value, timestamp, anchors);
            }
        }
    }

    private static SampleProjection ToProjection(Submission submission, Sample s, SchemaValue def, CadenceAnchors? anchors)
    {
        var (start, end) = CadenceCalculator.BucketFor(def.Cadence, s.Timestamp, anchors);

        var p = new SampleProjection
        {
            SubmissionId = submission.Id,
            ServiceAccountId = submission.ServiceAccountId,
            ServiceName = submission.ServiceName ?? string.Empty,
            SchemaName = s.SchemaName,
            ValueName = s.ValueName,
            ValueType = def.Type,
            Timestamp = DateTime.SpecifyKind(s.Timestamp, DateTimeKind.Utc),
            SubmittedAt = DateTime.SpecifyKind(submission.SubmittedAt, DateTimeKind.Utc),
            Note = s.Note,
            Cadence = def.Cadence,
            PeriodStart = start,
            PeriodEnd = end,
            IsDerived = false,
        };

        ApplyTypedValue(p, def.Type, s.Value);
        return p;
    }

    private static SampleProjection ToDerivedProjection(
        Submission submission,
        string schemaName,
        SchemaValue def,
        object value,
        DateTime timestamp,
        CadenceAnchors? anchors)
    {
        var ts = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);
        var (start, end) = CadenceCalculator.BucketFor(def.Cadence, ts, anchors);

        var p = new SampleProjection
        {
            SubmissionId = submission.Id,
            ServiceAccountId = submission.ServiceAccountId,
            ServiceName = submission.ServiceName ?? string.Empty,
            SchemaName = schemaName,
            ValueName = def.Name,
            ValueType = def.Type,
            Timestamp = ts,
            SubmittedAt = DateTime.SpecifyKind(submission.SubmittedAt, DateTimeKind.Utc),
            Note = null,
            Cadence = def.Cadence,
            PeriodStart = start,
            PeriodEnd = end,
            IsDerived = true,
        };

        ApplyTypedValue(p, def.Type, value);
        return p;
    }

    private static void ApplyTypedValue(SampleProjection p, SchemaValueType type, object? value)
    {
        switch (type)
        {
            case SchemaValueType.String: p.StringValue = value as string ?? value?.ToString(); break;
            case SchemaValueType.Integer: p.IntegerValue = SampleValueCoercion.ToLong(value); break;
            case SchemaValueType.Number: p.NumberValue = SampleValueCoercion.ToDouble(value); break;
            case SchemaValueType.Date: p.DateValue = SampleValueCoercion.ToDate(value); break;
            case SchemaValueType.Boolean: p.BooleanValue = SampleValueCoercion.ToBoolean(value); break;
        }
    }
}
