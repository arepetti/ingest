using Ingest.Infrastructure.Validation;

namespace Ingest.Tests;

public class NCalcExpressionEvaluatorTests
{
    private static readonly IReadOnlyDictionary<string, object?> NoVars = new Dictionary<string, object?>();

    [Fact]
    public void True_means_valid()
    {
        var ev = new NCalcExpressionEvaluator();
        var r = ev.EvaluateValidation("value >= 0 and value <= 100",
            new Dictionary<string, object?> { ["value"] = 42 });
        Assert.True(r.IsValid);
        Assert.Null(r.ErrorMessage);
    }

    [Fact]
    public void False_means_invalid_without_message()
    {
        var ev = new NCalcExpressionEvaluator();
        var r = ev.EvaluateValidation("value >= 0 and value <= 100",
            new Dictionary<string, object?> { ["value"] = 250 });
        Assert.False(r.IsValid);
        Assert.Null(r.ErrorMessage);
    }

    [Fact]
    public void Non_empty_string_is_an_error_message()
    {
        var ev = new NCalcExpressionEvaluator();
        var r = ev.EvaluateValidation(
            "if(value > 100, 'too high', null)",
            new Dictionary<string, object?> { ["value"] = 150 });
        Assert.False(r.IsValid);
        Assert.Equal("too high", r.ErrorMessage);
    }

    [Fact]
    public void Null_and_empty_string_are_valid()
    {
        var ev = new NCalcExpressionEvaluator();

        // A null parameter value is treated as "no opinion" → valid.
        var n = ev.EvaluateValidation("missing", new Dictionary<string, object?> { ["missing"] = null });
        Assert.True(n.IsValid);

        var e = ev.EvaluateValidation("''", NoVars);
        Assert.True(e.IsValid);
    }

    [Fact]
    public void Null_literal_in_if_branch_is_accepted()
    {
        var ev = new NCalcExpressionEvaluator();

        // NCalc's grammar has no `null` literal, but the docs and several sample schemas use
        // `if(condition, 'message', null)` to mean "no error otherwise". The evaluator exposes
        // a sentinel `null` parameter so the rule evaluates to a real null on the else branch.
        var ok = ev.EvaluateValidation(
            "if(value < 0, 'Headcount cannot be negative.', null)",
            new Dictionary<string, object?> { ["value"] = 11 });
        Assert.True(ok.IsValid);
        Assert.Null(ok.ErrorMessage);

        var bad = ev.EvaluateValidation(
            "if(value < 0, 'Headcount cannot be negative.', null)",
            new Dictionary<string, object?> { ["value"] = -3 });
        Assert.False(bad.IsValid);
        Assert.Equal("Headcount cannot be negative.", bad.ErrorMessage);
    }

    [Fact]
    public void Submission_validation_with_null_branch_does_not_throw()
    {
        var ev = new NCalcExpressionEvaluator();

        // Mirrors the schema-level rule in examples/schemas/generic.json (weekly_workforce):
        // `if(sick_leave > employees_active, 'Sick-leave count (…) cannot exceed …', null)`
        var ok = ev.EvaluateValidation(
            "if(sick_leave > employees_active, 'too many sick leaves', null)",
            new Dictionary<string, object?> { ["sick_leave"] = 1, ["employees_active"] = 11 });
        Assert.True(ok.IsValid);
    }

    [Fact]
    public void Cross_value_rule_uses_variables_from_other_values()
    {
        var ev = new NCalcExpressionEvaluator();
        var ok = ev.EvaluateValidation("revenue >= expenses",
            new Dictionary<string, object?> { ["revenue"] = 1000, ["expenses"] = 750 });
        Assert.True(ok.IsValid);

        var bad = ev.EvaluateValidation("if(expenses > revenue, 'expenses exceed revenue', true)",
            new Dictionary<string, object?> { ["revenue"] = 100, ["expenses"] = 200 });
        Assert.False(bad.IsValid);
        Assert.Equal("expenses exceed revenue", bad.ErrorMessage);
    }

    [Fact]
    public void Builtin_date_functions_dont_clash_with_user_variables()
    {
        var ev = new NCalcExpressionEvaluator();
        // A user value happens to be called 'month' — that must NOT clash with the month() function.
        var r = ev.EvaluateValidation(
            "month == 12 and month(now()) >= 1",
            new Dictionary<string, object?> { ["month"] = 12 });
        Assert.True(r.IsValid);
    }

    [Fact]
    public void Custom_function_is_evaluated()
    {
        var ev = new NCalcExpressionEvaluator();
        var customFns = new Dictionary<string, Func<object?[], object?>>(StringComparer.OrdinalIgnoreCase)
        {
            ["serviceName"] = _ => "alpha",
        };
        var r = ev.EvaluateValidation("serviceName() == 'alpha'", NoVars, customFns);
        Assert.True(r.IsValid);
    }

    [Fact]
    public void Bracketed_identifier_with_dot_resolves_to_bound_parameter()
    {
        // The validator exposes per-value bounds as `[name.minimum]` / `[name.maximum]`. The
        // bracket form is the only way NCalc accepts the `.` character in an identifier; once
        // stripped, the lookup key matches the parameter name we register.
        var ev = new NCalcExpressionEvaluator();
        var p = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["tonnes_collected"] = 180,
            ["tonnes_collected.maximum"] = 200,
        };
        var ok = ev.EvaluateValidation(
            "if([tonnes_collected.maximum] - tonnes_collected < 5, 'Within 5 of cap.', null)",
            p);
        Assert.True(ok.IsValid);

        p["tonnes_collected"] = 198;
        var warn = ev.EvaluateValidation(
            "if([tonnes_collected.maximum] - tonnes_collected < 5, 'Within 5 of cap.', null)",
            p);
        Assert.False(warn.IsValid);
        Assert.Equal("Within 5 of cap.", warn.ErrorMessage);
    }

    [Fact]
    public void Average_over_numbers()
    {
        var ev = new NCalcExpressionEvaluator();
        var result = ev.Evaluate("average(2, 4, 6)", NoVars);
        Assert.Equal(4d, result);
    }

    [Fact]
    public void Average_coerces_booleans_and_ignores_nulls()
    {
        var ev = new NCalcExpressionEvaluator();
        var p = new Dictionary<string, object?> { ["flag"] = true, ["missing"] = null };
        var result = ev.Evaluate("average(2, flag, missing, 4)", p);
        Assert.Equal((2d + 1d + 4d) / 3d, result);
    }

    [Fact]
    public void Average_with_no_numeric_args_returns_null()
    {
        var ev = new NCalcExpressionEvaluator();
        Assert.Null(ev.Evaluate("average()", NoVars));
        Assert.Null(ev.Evaluate("average(missing)", new Dictionary<string, object?> { ["missing"] = null }));
    }

    [Fact]
    public void Average_rejects_non_numeric_non_boolean()
    {
        var ev = new NCalcExpressionEvaluator();
        Assert.Throws<InvalidOperationException>(() => ev.Evaluate("average('x')", NoVars));
        Assert.Throws<InvalidOperationException>(() => ev.Evaluate("average(now())", NoVars));
    }
}
