using Ingest.Core.Abstractions;
using Ingest.Core.Entities;

namespace Ingest.Infrastructure.Services;

/// <summary>
/// Evaluates calculated schema values against a sibling-value context. Used by the submission
/// validator (so rules can reference derived values) and by <see cref="SampleProjectionBuilder"/>
/// (to materialise derived rows into the read model).
/// </summary>
public static class DerivedValueCalculator
{
    /// <summary>
    /// Compute every calculated value on <paramref name="schema"/> and merge the results into
    /// <paramref name="context"/>. Evaluation follows dependency order among calculated values;
    /// cycles and evaluation errors yield <c>null</c> for the affected value(s).
    /// </summary>
    public static void ComputeInto(
        Schema schema,
        IDictionary<string, object?> context,
        IExpressionEvaluator evaluator)
    {
        var calculated = schema.Values.Where(v => v.IsCalculated && !string.IsNullOrWhiteSpace(v.Expression)).ToList();
        if (calculated.Count == 0) return;

        var order = TopologicalOrder(calculated, schema);
        foreach (var def in order)
        {
            object? raw;
            try { raw = evaluator.Evaluate(def.Expression!, (IReadOnlyDictionary<string, object?>)context); }
            catch { raw = null; }

            var coerced = SampleValueCoercion.CoerceToType(raw, def.Type);
            context[def.Name] = coerced;
        }
    }

    /// <summary>
    /// Build a fresh context from submitted values (plus numeric bound keys) and compute calculated values.
    /// </summary>
    public static Dictionary<string, object?> Compute(
        Schema schema,
        IReadOnlyDictionary<string, object?> submittedValues,
        IExpressionEvaluator evaluator)
    {
        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in schema.Values)
        {
            context[v.Name] = submittedValues.TryGetValue(v.Name, out var x) ? x : null;
            if (v.Type is SchemaValueType.Integer or SchemaValueType.Number)
            {
                if (v.Min is { } m) context[$"{v.Name}.minimum"] = m;
                if (v.Max is { } M) context[$"{v.Name}.maximum"] = M;
            }
        }

        ComputeInto(schema, context, evaluator);
        return context;
    }

    /// <summary>Return calculated value names only, in dependency order.</summary>
    internal static IReadOnlyList<SchemaValue> TopologicalOrder(IReadOnlyList<SchemaValue> calculated, Schema schema)
    {
        try
        {
            return TopologicalOrderCore(calculated, schema);
        }
        catch
        {
            // Identifier extraction can fail on legacy/direct-DB expressions that still parse
            // and evaluate; fall back to declaration order and let per-value Evaluate handle errors.
            return calculated.ToList();
        }
    }

    private static IReadOnlyList<SchemaValue> TopologicalOrderCore(IReadOnlyList<SchemaValue> calculated, Schema schema)
    {
        var byName = schema.Values.ToDictionary(v => v.Name, StringComparer.OrdinalIgnoreCase);
        var calcNames = calculated.Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var deps = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in calculated)
        {
            var refs = ExpressionReferences.ForCalculatedDependencies(v.Expression!, schema, calcNames);
            deps[v.Name] = refs.Where(r => calcNames.Contains(r) && !r.Equals(v.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var order = new List<SchemaValue>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool Visit(string name)
        {
            if (visited.Contains(name)) return true;
            if (stack.Contains(name)) return false;
            stack.Add(name);
            if (deps.TryGetValue(name, out var d))
            {
                foreach (var dep in d)
                {
                    if (!Visit(dep)) return false;
                }
            }
            stack.Remove(name);
            visited.Add(name);
            if (byName.TryGetValue(name, out var def) && def.IsCalculated)
                order.Add(def);
            return true;
        }

        foreach (var v in calculated)
        {
            if (!Visit(v.Name))
            {
                // Cycle: fall back to declaration order with a fixpoint pass (max passes = count).
                order.Clear();
                var working = calculated.ToList();
                for (var pass = 0; pass < working.Count; pass++)
                {
                    var progressed = false;
                    foreach (var def in working)
                    {
                        if (order.Any(x => x.Name.Equals(def.Name, StringComparison.OrdinalIgnoreCase))) continue;
                        var ready = deps[def.Name].All(d => order.Any(x => x.Name.Equals(d, StringComparison.OrdinalIgnoreCase)));
                        if (ready)
                        {
                            order.Add(def);
                            progressed = true;
                        }
                    }
                    if (!progressed) break;
                }
                foreach (var def in working)
                {
                    if (order.All(x => !x.Name.Equals(def.Name, StringComparison.OrdinalIgnoreCase)))
                        order.Add(def);
                }
                break;
            }
        }

        return order;
    }
}
