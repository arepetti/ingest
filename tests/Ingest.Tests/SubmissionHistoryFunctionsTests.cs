using Ingest.Infrastructure.Validation;

namespace Ingest.Tests;

/// <summary>
/// Coverage for the <c>latest()</c> / <c>previous()</c> history functions exposed to validation
/// rules. The pre-fetched value maps are built by the validator from the live sample projection;
/// here we feed the maps directly and assert how <see cref="SubmissionValidator.BuildHistoryFunctions"/>
/// resolves names, fallbacks, and the value-level no-argument shorthand through the real NCalc
/// evaluator.
/// </summary>
public class SubmissionHistoryFunctionsTests
{
    private static readonly NCalcExpressionEvaluator Eval = new();
    private static readonly IReadOnlyDictionary<string, object?> NoVars = new Dictionary<string, object?>();

    private static IReadOnlyDictionary<string, object?> Map(params (string name, object? value)[] entries)
    {
        var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in entries) d[name] = value;
        return d;
    }

    private static object? Run(
        string expression,
        IReadOnlyDictionary<string, object?> latest,
        IReadOnlyDictionary<string, object?> previous,
        string? currentValueName,
        IReadOnlyDictionary<string, object?>? vars = null)
    {
        var fns = SubmissionValidator.BuildHistoryFunctions(latest, previous, currentValueName);
        return Eval.Evaluate(expression, vars ?? NoVars, fns);
    }

    [Fact]
    public void Latest_returns_value_for_named_value()
    {
        var latest = Map(("tonnes", 180L));
        var r = Run("latest('tonnes')", latest, NoVars, currentValueName: null);
        Assert.Equal(180L, r);
    }

    [Fact]
    public void Previous_returns_value_for_named_value()
    {
        var previous = Map(("tonnes", 150L));
        var r = Run("previous('tonnes')", NoVars, previous, currentValueName: null);
        Assert.Equal(150L, r);
    }

    [Fact]
    public void Missing_history_is_null()
    {
        var r = Run("latest('tonnes')", NoVars, NoVars, currentValueName: null);
        Assert.Null(r);
    }

    [Fact]
    public void Fallback_used_when_history_is_missing()
    {
        var r = Run("latest('reading', 0)", NoVars, NoVars, currentValueName: null);
        Assert.Equal(0, r);

        var p = Run("previous('reading', 0)", NoVars, NoVars, currentValueName: null);
        Assert.Equal(0, p);
    }

    [Fact]
    public void Fallback_ignored_when_history_present()
    {
        var latest = Map(("reading", 42L));
        var r = Run("latest('reading', 0)", latest, NoVars, currentValueName: null);
        Assert.Equal(42L, r);
    }

    [Fact]
    public void Value_level_no_argument_resolves_to_current_value()
    {
        var latest = Map(("tonnes", 200L));
        var r = Run("latest()", latest, NoVars, currentValueName: "tonnes");
        Assert.Equal(200L, r);
    }

    [Fact]
    public void Value_level_no_argument_with_fallback_uses_current_value()
    {
        // A single non-string argument is treated as the fallback for the current value.
        var r = Run("latest(0)", NoVars, NoVars, currentValueName: "reading");
        Assert.Equal(0, r);

        var latest = Map(("reading", 9L));
        var hit = Run("latest(0)", latest, NoVars, currentValueName: "reading");
        Assert.Equal(9L, hit);
    }

    [Fact]
    public void Schema_level_no_argument_has_no_current_value()
    {
        // currentValueName is null at schema level → no-arg call has nothing to resolve to.
        var r = Run("latest()", Map(("tonnes", 1L)), NoVars, currentValueName: null);
        Assert.Null(r);
    }

    [Fact]
    public void Lookup_is_case_insensitive_on_value_name()
    {
        var latest = Map(("Tonnes", 5L));
        var r = Run("latest('tonnes')", latest, NoVars, currentValueName: null);
        Assert.Equal(5L, r);
    }

    [Fact]
    public void Validation_rule_can_compare_current_against_latest()
    {
        var latest = Map(("tonnes", 100.0));
        var vars = Map(("tonnes", 120.0));

        // 120 is more than 10% above 100 → rule rejects with its message.
        var bad = Eval.EvaluateValidation(
            "if(not isNull(latest('tonnes')) and tonnes > latest('tonnes') * 1.1, 'more than 10% above last reported', null)",
            vars,
            SubmissionValidator.BuildHistoryFunctions(latest, NoVars, "tonnes"));
        Assert.False(bad.IsValid);
        Assert.Equal("more than 10% above last reported", bad.ErrorMessage);

        // Within tolerance → valid.
        var ok = Eval.EvaluateValidation(
            "if(not isNull(latest('tonnes')) and tonnes > latest('tonnes') * 1.1, 'more than 10% above last reported', null)",
            Map(("tonnes", 105.0)),
            SubmissionValidator.BuildHistoryFunctions(latest, NoVars, "tonnes"));
        Assert.True(ok.IsValid);
    }

    [Fact]
    public void Non_decreasing_counter_rule_uses_previous_with_fallback()
    {
        var previous = Map(("reading", 500L));
        var fns = SubmissionValidator.BuildHistoryFunctions(NoVars, previous, "reading");

        var backwards = Eval.EvaluateValidation(
            "if(reading < previous('reading', 0), 'reading is lower than last period', null)",
            Map(("reading", 480L)), fns);
        Assert.False(backwards.IsValid);

        var forwards = Eval.EvaluateValidation(
            "if(reading < previous('reading', 0), 'reading is lower than last period', null)",
            Map(("reading", 520L)), fns);
        Assert.True(forwards.IsValid);

        // No history → fallback 0 means any non-negative reading is accepted.
        var firstEver = Eval.EvaluateValidation(
            "if(reading < previous('reading', 0), 'reading is lower than last period', null)",
            Map(("reading", 10L)),
            SubmissionValidator.BuildHistoryFunctions(NoVars, NoVars, "reading"));
        Assert.True(firstEver.IsValid);
    }
}
