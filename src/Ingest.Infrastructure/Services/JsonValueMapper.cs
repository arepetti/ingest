using System.Text.Json;
using Ingest.Core.Entities;

namespace Ingest.Infrastructure.Services;

/// <summary>
/// Coerces the raw <see cref="JsonElement"/> payload of a submission sample into a .NET object
/// of the type declared by the corresponding <see cref="SchemaValueType"/>. When the target type
/// isn't known (e.g. the sample references a non-existent value), the raw JSON shape is
/// preserved so the validator can produce a meaningful error.
/// </summary>
public static class JsonValueMapper
{
    /// <summary>Map a JSON value onto the CLR type expected by the schema.</summary>
    /// <param name="element">Raw JSON element from the request body; <c>null</c>/undefined is treated as "no value".</param>
    /// <param name="type">Expected type, or <c>null</c> if not resolvable. When null, the raw value shape is returned.</param>
    /// <returns>A string/long/double/DateTime/bool/null, or the raw value if coercion isn't possible.</returns>
    public static object? MapValue(JsonElement? element, SchemaValueType? type)
    {
        if (element is not JsonElement el || el.ValueKind == JsonValueKind.Null || el.ValueKind == JsonValueKind.Undefined)
            return null;

        if (type is null)
            return Raw(el);

        return type switch
        {
            SchemaValueType.String => el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString(),
            SchemaValueType.Integer => el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var l) ? l :
                                       el.ValueKind == JsonValueKind.String && long.TryParse(el.GetString(), out var ls) ? ls :
                                       (object?)Raw(el),
            SchemaValueType.Number => el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d) ? d :
                                      el.ValueKind == JsonValueKind.String && double.TryParse(el.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var ds) ? ds :
                                      (object?)Raw(el),
            SchemaValueType.Date => el.ValueKind == JsonValueKind.String && DateTime.TryParse(el.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt) ? dt :
                                    (object?)Raw(el),
            SchemaValueType.Boolean => el.ValueKind == JsonValueKind.True ? true :
                                       el.ValueKind == JsonValueKind.False ? false :
                                       (object?)Raw(el),
            _ => Raw(el),
        };
    }

    private static object? Raw(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => el.ToString(),
    };
}
