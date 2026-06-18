using Ingest.Core.Entities;
using Ingest.Infrastructure.Integrations;
using Ingest.Infrastructure.Validation;

namespace Ingest.Tests;

/// <summary>
/// Unit tests for <see cref="TeamsCardBuilder"/> — the part of the Teams integration that decides
/// which schema values to ask for and turns answers into samples. The focus is the conditional
/// gating: a value hidden/disabled (statically or via <c>VisibleIf</c>/<c>EnabledIf</c>) is never
/// asked, and a dependent value only becomes "outstanding" once an earlier answer activates it
/// (the sequential prompting the bot relies on). Uses the real NCalc evaluator so the gating is
/// exercised exactly as production does it.
/// </summary>
public class TeamsCardBuilderTests
{
    private static TeamsCardBuilder Builder() => new(new NCalcExpressionEvaluator());

    private static SchemaValue Value(
        string name,
        SchemaValueType type = SchemaValueType.Number,
        bool required = true,
        bool enabled = true,
        string? visibleIf = null,
        string? enabledIf = null) => new()
    {
        Name = name,
        Type = type,
        Required = required,
        Enabled = enabled,
        VisibleIf = visibleIf,
        EnabledIf = enabledIf,
    };

    private static Schema SchemaWith(params SchemaValue[] values) => new()
    {
        Name = "kpi",
        Values = values.ToList(),
    };

    private static IReadOnlyDictionary<string, object?> Answers(Schema schema, Dictionary<string, object?> raw) =>
        TeamsCardBuilder.CoerceAnswers(schema, raw);

    private static ISet<string> Answered(params string[] names) =>
        new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Statically_disabled_value_is_never_active()
    {
        var schema = SchemaWith(Value("a"), Value("b", enabled: false));
        var active = Builder().ActiveValues(schema, new Dictionary<string, object?>());

        Assert.Contains(active, v => v.Name == "a");
        Assert.DoesNotContain(active, v => v.Name == "b");
    }

    [Fact]
    public void EnabledIf_false_drops_the_value()
    {
        var schema = SchemaWith(Value("a"), Value("b", enabledIf: "1 == 2"));
        var active = Builder().ActiveValues(schema, new Dictionary<string, object?>());

        Assert.Contains(active, v => v.Name == "a");
        Assert.DoesNotContain(active, v => v.Name == "b");
    }

    [Fact]
    public void Conditional_value_is_gated_until_an_earlier_answer_activates_it()
    {
        // "detail" is only visible when mode == 'full'. The bot asks sequentially, so before the
        // gate is answered the dependent field must not be in the "outstanding" set.
        var schema = SchemaWith(
            Value("mode", SchemaValueType.String),
            Value("detail", SchemaValueType.Number, visibleIf: "mode == 'full'"));
        var b = Builder();

        // Round 1: nothing answered yet → only the gate is asked.
        var round1 = b.OutstandingRequired(schema, new Dictionary<string, object?>(), Answered());
        Assert.Contains(round1, v => v.Name == "mode");
        Assert.DoesNotContain(round1, v => v.Name == "detail");

        // Round 2: gate satisfied → the dependent field becomes outstanding.
        var full = Answers(schema, new() { ["mode"] = "full" });
        var round2 = b.OutstandingRequired(schema, full, Answered("mode"));
        Assert.Contains(round2, v => v.Name == "detail");

        // Gate NOT satisfied → the dependent field stays hidden and nothing is left to ask.
        var partial = Answers(schema, new() { ["mode"] = "partial" });
        var roundAlt = b.OutstandingRequired(schema, partial, Answered("mode"));
        Assert.Empty(roundAlt);
    }

    [Fact]
    public void Answered_required_values_drop_out_of_the_outstanding_set()
    {
        var schema = SchemaWith(Value("a"), Value("b"));
        var answers = Answers(schema, new() { ["a"] = "5" });

        var outstanding = Builder().OutstandingRequired(schema, answers, Answered("a"));

        Assert.DoesNotContain(outstanding, v => v.Name == "a");
        Assert.Contains(outstanding, v => v.Name == "b");
    }

    [Fact]
    public void Optional_values_are_not_outstanding()
    {
        var schema = SchemaWith(Value("a"), Value("opt", required: false));
        var outstanding = Builder().OutstandingRequired(schema, new Dictionary<string, object?>(), Answered());

        Assert.Contains(outstanding, v => v.Name == "a");
        Assert.DoesNotContain(outstanding, v => v.Name == "opt");
    }

    [Fact]
    public void CoerceAnswers_produces_typed_scalars()
    {
        var schema = SchemaWith(
            Value("count", SchemaValueType.Integer),
            Value("ratio", SchemaValueType.Number),
            Value("flag", SchemaValueType.Boolean),
            Value("name", SchemaValueType.String));

        var typed = TeamsCardBuilder.CoerceAnswers(schema, new Dictionary<string, object?>
        {
            ["count"] = "12",
            ["ratio"] = "3.5",
            ["flag"] = "true",
            ["name"] = "alpha",
        });

        Assert.Equal(12L, typed["count"]);
        Assert.Equal(3.5, typed["ratio"]);
        Assert.Equal(true, typed["flag"]);
        Assert.Equal("alpha", typed["name"]);
    }

    [Fact]
    public void BuildSamples_drops_inactive_values_and_never_sets_a_note()
    {
        // "detail" is hidden because mode != 'full', so its stray answer must not become a sample.
        var schema = SchemaWith(
            Value("mode", SchemaValueType.String),
            Value("detail", SchemaValueType.Number, visibleIf: "mode == 'full'"));

        var answers = Answers(schema, new() { ["mode"] = "partial", ["detail"] = "99" });
        var samples = Builder().BuildSamples(schema, answers, DateTime.UtcNow);

        Assert.Contains(samples, sa => sa.ValueName == "mode");
        Assert.DoesNotContain(samples, sa => sa.ValueName == "detail");
        Assert.All(samples, sa => Assert.Null(sa.Note));
    }
}
