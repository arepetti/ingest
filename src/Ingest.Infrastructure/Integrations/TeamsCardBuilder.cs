using System.Globalization;
using System.Text.Json;
using Ingest.Core.Abstractions;
using Ingest.Core.Entities;

namespace Ingest.Infrastructure.Integrations;

/// <summary>
/// Builds the Adaptive Card prompts for a Teams integration and maps the answers a user submits
/// back into typed samples. Conditional fields are honoured the same way the submission editor does
/// it: each value's <see cref="SchemaValue.VisibleIf"/> / <see cref="SchemaValue.EnabledIf"/> rule
/// is evaluated (via the same <see cref="IExpressionEvaluator"/> the validator uses) against the
/// answers gathered so far, so a value that resolves hidden or disabled is skipped. Note inputs are
/// never rendered — samples are always submitted with <c>note: null</c>.
/// </summary>
public sealed class TeamsCardBuilder
{
    /// <summary>Action verb carried on the card's submit action; the bot endpoint dispatches on it.</summary>
    public const string SubmitVerb = "ingest.teams.submit";

    private readonly IExpressionEvaluator _evaluator;

    /// <summary>Create a new <see cref="TeamsCardBuilder"/>.</summary>
    public TeamsCardBuilder(IExpressionEvaluator evaluator) => _evaluator = evaluator;

    /// <summary>
    /// Values that are currently "active" for the given answers: statically enabled and neither
    /// hidden by <c>VisibleIf</c> nor disabled by <c>EnabledIf</c> in this context. Mirrors the
    /// editor's gating and the validator's discard rules.
    /// </summary>
    public IReadOnlyList<SchemaValue> ActiveValues(Schema schema, IReadOnlyDictionary<string, object?> answers)
    {
        var context = BuildRuleContext(schema, answers);
        var active = new List<SchemaValue>();
        foreach (var v in schema.Values)
        {
            if (!v.Enabled) continue;
            if (IsConditionFalse(v.VisibleIf, context)) continue;
            if (IsConditionFalse(v.EnabledIf, context)) continue;
            active.Add(v);
        }
        return active;
    }

    /// <summary>
    /// Required, active values that have not yet been answered. This is the per-round "what to ask"
    /// set: as answers arrive they may activate or deactivate further conditional values, so the
    /// caller re-runs this after every submit until it returns empty.
    /// </summary>
    public IReadOnlyList<SchemaValue> OutstandingRequired(
        Schema schema,
        IReadOnlyDictionary<string, object?> answers,
        ISet<string> answeredNames) =>
        ActiveValues(schema, answers)
            .Where(v => v.Required && !answeredNames.Contains(v.Name))
            .ToList();

    /// <summary>
    /// Build an Adaptive Card asking for <paramref name="ask"/>, carrying <paramref name="answers"/>
    /// (already gathered) as hidden action data so the next round reconstructs the full context.
    /// Returned as a serialisable object graph (no Bot SDK types).
    /// </summary>
    public object BuildPromptCard(
        Integration integration,
        Schema schema,
        Guid serviceId,
        string serviceLabel,
        string periodLabel,
        IReadOnlyDictionary<string, object?> answers,
        IReadOnlyList<SchemaValue> ask)
    {
        var body = new List<object>
        {
            new Dictionary<string, object?>
            {
                ["type"] = "TextBlock", ["size"] = "Medium", ["weight"] = "Bolder", ["wrap"] = true,
                ["text"] = $"KPI data needed: {DisplaySchema(schema)}",
            },
            new Dictionary<string, object?>
            {
                ["type"] = "TextBlock", ["isSubtle"] = true, ["wrap"] = true, ["spacing"] = "None",
                ["text"] = $"{serviceLabel} • {periodLabel}",
            },
        };

        foreach (var v in ask)
            body.Add(BuildInputBlock(v));

        return new Dictionary<string, object?>
        {
            ["type"] = "AdaptiveCard",
            ["version"] = "1.4",
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["body"] = body,
            ["actions"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "Action.Execute",
                    ["title"] = "Submit",
                    ["verb"] = SubmitVerb,
                    ["data"] = new Dictionary<string, object?>
                    {
                        ["integrationId"] = integration.Id.ToString(),
                        ["serviceId"] = serviceId.ToString(),
                        ["schema"] = schema.Name,
                        ["answers"] = answers.ToDictionary(kv => kv.Key, kv => SerialiseAnswer(kv.Value)),
                    },
                    // Fallback so older Teams clients still render a usable button.
                    ["fallback"] = new Dictionary<string, object?>
                    {
                        ["type"] = "Action.Submit",
                        ["title"] = "Submit",
                        ["data"] = new Dictionary<string, object?> { ["verb"] = SubmitVerb },
                    },
                },
            },
        };
    }

    /// <summary>Build a simple result card shown after a submit (success + any warnings, or errors).</summary>
    public object BuildResultCard(string title, IReadOnlyList<string> messages, bool isError)
    {
        var body = new List<object>
        {
            new Dictionary<string, object?>
            {
                ["type"] = "TextBlock", ["size"] = "Medium", ["weight"] = "Bolder", ["wrap"] = true,
                ["color"] = isError ? "Attention" : "Good", ["text"] = title,
            },
        };
        foreach (var m in messages)
            body.Add(new Dictionary<string, object?> { ["type"] = "TextBlock", ["wrap"] = true, ["text"] = "• " + m });

        return new Dictionary<string, object?>
        {
            ["type"] = "AdaptiveCard",
            ["version"] = "1.4",
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["body"] = body,
        };
    }

    /// <summary>
    /// Map gathered answers to typed samples for the active values. Values that aren't active in the
    /// final context are dropped; notes are always null. <paramref name="timestamp"/> is the
    /// measurement time stamped on every sample.
    /// </summary>
    public List<SampleInput> BuildSamples(
        Schema schema,
        IReadOnlyDictionary<string, object?> answers,
        DateTime timestamp)
    {
        var active = ActiveValues(schema, answers).ToDictionary(v => v.Name, StringComparer.OrdinalIgnoreCase);
        var samples = new List<SampleInput>();
        foreach (var (name, raw) in answers)
        {
            if (!active.TryGetValue(name, out var def)) continue;
            var json = ToJsonElement(def.Type, raw);
            if (json is null) continue;
            samples.Add(new SampleInput(schema.Name, def.Name, json, timestamp, null));
        }
        return samples;
    }

    /// <summary>
    /// Coerce the raw answer strings that arrive from a card submit into the typed objects the rule
    /// engine expects (numbers, bools, dates), keyed by value name plus the numeric bound keys —
    /// the same shape <c>SubmissionValidator.BuildRuleContext</c> produces.
    /// </summary>
    public static Dictionary<string, object?> CoerceAnswers(Schema schema, IReadOnlyDictionary<string, object?> rawAnswers)
    {
        var typed = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in schema.Values)
        {
            if (rawAnswers.TryGetValue(v.Name, out var raw) && raw is not null)
                typed[v.Name] = CoerceScalar(v.Type, raw);
        }
        return typed;
    }

    // ── internals ──────────────────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> BuildRuleContext(Schema schema, IReadOnlyDictionary<string, object?> answers)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in schema.Values)
        {
            parameters[v.Name] = answers.TryGetValue(v.Name, out var x) ? x : null;
            if (v.Type is SchemaValueType.Integer or SchemaValueType.Number)
            {
                if (v.Min is { } m) parameters[$"{v.Name}.minimum"] = m;
                if (v.Max is { } M) parameters[$"{v.Name}.maximum"] = M;
            }
        }
        return parameters;
    }

    private bool IsConditionFalse(string? expression, IReadOnlyDictionary<string, object?> context)
    {
        if (string.IsNullOrWhiteSpace(expression)) return false;
        try { return !_evaluator.EvaluateValidation(expression, context).IsValid; }
        catch { return false; } // a broken rule never hides a field (server stays authoritative)
    }

    private static object BuildInputBlock(SchemaValue v)
    {
        var label = DisplayValue(v) + (string.IsNullOrWhiteSpace(v.Unit) ? "" : $" ({v.Unit})");
        var block = new Dictionary<string, object?> { ["type"] = "Input.Text", ["id"] = v.Name, ["label"] = label };

        switch (v.Type)
        {
            case SchemaValueType.Integer:
            case SchemaValueType.Number:
                block["type"] = "Input.Number";
                if (v.Min is { } min) block["min"] = min;
                if (v.Max is { } max) block["max"] = max;
                break;
            case SchemaValueType.Date:
                block["type"] = "Input.Date";
                break;
            case SchemaValueType.Boolean:
                block["type"] = "Input.Toggle";
                block["title"] = label;
                block.Remove("label");
                block["valueOn"] = "true";
                block["valueOff"] = "false";
                break;
            case SchemaValueType.String:
                if (v.MaxLength is { } ml) block["maxLength"] = ml;
                if (!string.IsNullOrWhiteSpace(v.RegexPattern)) block["regex"] = v.RegexPattern;
                break;
        }

        if (v.Required && v.Type != SchemaValueType.Boolean)
            block["isRequired"] = true;
        if (!string.IsNullOrWhiteSpace(v.Description))
            block["placeholder"] = v.Description;
        return block;
    }

    private static object? SerialiseAnswer(object? value) => value switch
    {
        null => null,
        bool b => b,
        DateTime d => d.ToString("o", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private static object? CoerceScalar(SchemaValueType type, object raw)
    {
        var s = raw as string ?? raw.ToString();
        if (string.IsNullOrWhiteSpace(s)) return null;
        return type switch
        {
            SchemaValueType.Integer => long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var l) ? l : null,
            SchemaValueType.Number => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null,
            SchemaValueType.Boolean => bool.TryParse(s, out var b) ? b : (object?)null,
            SchemaValueType.Date => DateTime.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt) ? dt : null,
            _ => s,
        };
    }

    private static JsonElement? ToJsonElement(SchemaValueType type, object? raw)
    {
        var scalar = raw is string s ? CoerceScalar(type, s) : raw;
        if (scalar is null) return null;
        return scalar switch
        {
            long l => JsonSerializer.SerializeToElement(l),
            double d => JsonSerializer.SerializeToElement(d),
            bool b => JsonSerializer.SerializeToElement(b),
            DateTime dt => JsonSerializer.SerializeToElement(dt.ToString("o", CultureInfo.InvariantCulture)),
            _ => JsonSerializer.SerializeToElement(scalar.ToString()),
        };
    }

    private static string DisplaySchema(Schema schema) =>
        string.IsNullOrWhiteSpace(schema.Label) ? schema.Name : schema.Label!;

    private static string DisplayValue(SchemaValue v) =>
        string.IsNullOrWhiteSpace(v.Label) ? v.Name : v.Label!;
}
