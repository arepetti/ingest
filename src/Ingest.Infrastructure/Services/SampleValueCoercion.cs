using Ingest.Core.Entities;

namespace Ingest.Infrastructure.Services;

/// <summary>
/// Shared coercion helpers for sample values — used when projecting submitted samples and when
/// coercing calculated-value expression results to their declared wire-type.
/// </summary>
internal static class SampleValueCoercion
{
    internal static long? ToLong(object? v) => v switch
    {
        long l => l,
        int i => i,
        double d when !double.IsNaN(d) => (long)d,
        decimal m => (long)m,
        string s when long.TryParse(s, out var p) => p,
        _ => null,
    };

    internal static double? ToDouble(object? v) => v switch
    {
        double d => double.IsNaN(d) || double.IsInfinity(d) ? null : d,
        float f => float.IsNaN(f) || float.IsInfinity(f) ? null : f,
        int i => i,
        long l => l,
        decimal m => (double)m,
        string s when double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var p) => p,
        _ => null,
    };

    internal static DateTime? ToDate(object? v) => v switch
    {
        DateTime dt => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
        DateTimeOffset dto => dto.UtcDateTime,
        string s when DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var p) => p,
        _ => null,
    };

    internal static bool? ToBoolean(object? v) => v switch
    {
        bool b => b,
        null => null,
        string s when bool.TryParse(s, out var p) => p,
        double d when !double.IsNaN(d) => d != 0,
        int i => i != 0,
        long l => l != 0,
        _ => null,
    };

    /// <summary>Coerce a raw expression result to the declared schema value type; uncoercible → null.</summary>
    internal static object? CoerceToType(object? raw, SchemaValueType type) => type switch
    {
        SchemaValueType.String => raw?.ToString(),
        SchemaValueType.Integer => ToLong(raw),
        SchemaValueType.Number => ToDouble(raw),
        SchemaValueType.Date => ToDate(raw),
        SchemaValueType.Boolean => ToBoolean(raw),
        _ => null,
    };
}
