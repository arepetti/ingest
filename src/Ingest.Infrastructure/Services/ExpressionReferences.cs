using Ingest.Core.Abstractions;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Validation;

namespace Ingest.Infrastructure.Services;

/// <summary>Extract identifier references from NCalc expressions for schema validation and dependency ordering.</summary>
internal static class ExpressionReferences
{
    private static readonly NCalcToJavaScriptTranslator DefaultTranslator = new();

    /// <summary>Identifiers referenced by a calculated-value expression that point at other calculated values.</summary>
    internal static IReadOnlyList<string> ForCalculatedDependencies(
        string expression,
        Schema schema,
        IReadOnlySet<string> calculatedNames,
        IExpressionTranslator? translator = null)
    {
        if (!TryGetIdentifiers(expression, out var ids, out _, translator))
            return Array.Empty<string>();

        var valueNames = schema.Values.Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ids
            .Where(id => valueNames.Contains(id) && calculatedNames.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Parse an expression and return the identifiers it references; false when translation fails.</summary>
    internal static bool TryGetIdentifiers(
        string expression,
        out IReadOnlyList<string> identifiers,
        out string? error,
        IExpressionTranslator? translator = null)
    {
        try
        {
            identifiers = (translator ?? DefaultTranslator).TranslateToJavaScript(expression).Identifiers;
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            identifiers = Array.Empty<string>();
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Validate every identifier in an expression resolves to a declared schema value or bound key.</summary>
    internal static IEnumerable<string> UnknownIdentifiers(string expression, Schema schema, IExpressionTranslator? translator = null)
    {
        if (!TryGetIdentifiers(expression, out var ids, out _, translator))
            yield break;

        var valueByName = schema.Values.ToDictionary(v => v.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            if (ReservedIdentifiers.Contains(id)) continue;
            if (valueByName.ContainsKey(id)) continue;
            if (IsBoundKey(id, valueByName, out _)) continue;
            yield return id;
        }
    }

    /// <summary>True when the expression references its own value name (direct self-reference).</summary>
    internal static bool ReferencesSelf(string expression, string valueName, IExpressionTranslator? translator = null)
    {
        if (!TryGetIdentifiers(expression, out var ids, out _, translator))
            return false;

        return ids.Any(id => id.Equals(valueName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True when the expression calls history-only functions (sibling-only calculated values must not use these).</summary>
    internal static bool ReferencesHistoryFunctions(string expression) =>
        expression.Contains("latest(", StringComparison.OrdinalIgnoreCase) ||
        expression.Contains("previous(", StringComparison.OrdinalIgnoreCase);

    internal static bool IsBoundKey(string identifier, IReadOnlyDictionary<string, SchemaValue> valueByName, out string? valueName)
    {
        valueName = null;
        const string minSuffix = ".minimum";
        const string maxSuffix = ".maximum";
        if (identifier.EndsWith(minSuffix, StringComparison.OrdinalIgnoreCase))
        {
            valueName = identifier[..^minSuffix.Length];
            return valueByName.TryGetValue(valueName, out var v)
                   && v.Type is SchemaValueType.Integer or SchemaValueType.Number;
        }
        if (identifier.EndsWith(maxSuffix, StringComparison.OrdinalIgnoreCase))
        {
            valueName = identifier[..^maxSuffix.Length];
            return valueByName.TryGetValue(valueName, out var v)
                   && v.Type is SchemaValueType.Integer or SchemaValueType.Number;
        }
        return false;
    }

    internal static bool HasCycleAmongCalculated(Schema schema, IExpressionTranslator? translator = null)
    {
        var calculated = schema.Values.Where(v => v.IsCalculated && !string.IsNullOrWhiteSpace(v.Expression)).ToList();
        if (calculated.Count == 0) return false;

        var calcNames = calculated.Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var deps = calculated.ToDictionary(
            v => v.Name,
            v => ForCalculatedDependencies(v.Expression!, schema, calcNames, translator)
                .Where(n => !n.Equals(v.Name, StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool Dfs(string name)
        {
            if (stack.Contains(name)) return true;
            if (visited.Contains(name)) return false;
            visited.Add(name);
            stack.Add(name);
            if (deps.TryGetValue(name, out var d))
            {
                foreach (var dep in d)
                {
                    if (Dfs(dep)) return true;
                }
            }
            stack.Remove(name);
            return false;
        }

        return calculated.Any(v => Dfs(v.Name));
    }

    private static readonly HashSet<string> ReservedIdentifiers = new(StringComparer.OrdinalIgnoreCase) { "null" };
}
