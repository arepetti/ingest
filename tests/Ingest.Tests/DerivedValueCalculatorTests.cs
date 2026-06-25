using Ingest.Core.Entities;
using Ingest.Infrastructure.Services;
using Ingest.Infrastructure.Validation;

namespace Ingest.Tests;

public class DerivedValueCalculatorTests
{
    private static readonly NCalcExpressionEvaluator Evaluator = new();

    private static Schema SchemaWith(params SchemaValue[] values) => new()
    {
        Name = "demo",
        Values = values.ToList(),
    };

    [Fact]
    public void Compute_single_derived_value()
    {
        var schema = SchemaWith(
            new SchemaValue { Name = "a", Type = SchemaValueType.Number, Cadence = Cadence.Weekly },
            new SchemaValue { Name = "total", Type = SchemaValueType.Number, Cadence = Cadence.Weekly, Kind = SchemaValueKind.Calculated, Expression = "a * 2" });

        var result = DerivedValueCalculator.Compute(schema, new Dictionary<string, object?> { ["a"] = 5d }, Evaluator);
        Assert.Equal(10d, result["total"]);
    }

    [Fact]
    public void Compute_chained_derived_values()
    {
        var schema = SchemaWith(
            new SchemaValue { Name = "a", Type = SchemaValueType.Number, Cadence = Cadence.Weekly },
            new SchemaValue { Name = "b", Type = SchemaValueType.Number, Cadence = Cadence.Weekly, Kind = SchemaValueKind.Calculated, Expression = "a + 1" },
            new SchemaValue { Name = "c", Type = SchemaValueType.Number, Cadence = Cadence.Weekly, Kind = SchemaValueKind.Calculated, Expression = "b * 2" });

        var result = DerivedValueCalculator.Compute(schema, new Dictionary<string, object?> { ["a"] = 3d }, Evaluator);
        Assert.Equal(4d, result["b"]);
        Assert.Equal(8d, result["c"]);
    }

    [Fact]
    public void Compute_null_when_input_missing()
    {
        var schema = SchemaWith(
            new SchemaValue { Name = "x", Type = SchemaValueType.Number, Cadence = Cadence.Weekly, Kind = SchemaValueKind.Calculated, Expression = "missing + 1" });

        var result = DerivedValueCalculator.Compute(schema, new Dictionary<string, object?>(), Evaluator);
        Assert.Null(result["x"]);
    }

    [Fact]
    public void Compute_coerces_integer_type()
    {
        var schema = SchemaWith(
            new SchemaValue { Name = "a", Type = SchemaValueType.Number, Cadence = Cadence.Weekly },
            new SchemaValue { Name = "n", Type = SchemaValueType.Integer, Cadence = Cadence.Weekly, Kind = SchemaValueKind.Calculated, Expression = "a + 0.9" });

        var result = DerivedValueCalculator.Compute(schema, new Dictionary<string, object?> { ["a"] = 1.2d }, Evaluator);
        Assert.Equal(2L, result["n"]);
    }

    [Fact]
    public void Compute_coerces_string_and_boolean_types()
    {
        var schema = SchemaWith(
            new SchemaValue { Name = "flag", Type = SchemaValueType.Boolean, Cadence = Cadence.Weekly },
            new SchemaValue { Name = "label", Type = SchemaValueType.String, Cadence = Cadence.Weekly, Kind = SchemaValueKind.Calculated, Expression = "if(flag, 'yes', 'no')" },
            new SchemaValue { Name = "score", Type = SchemaValueType.Boolean, Cadence = Cadence.Weekly, Kind = SchemaValueKind.Calculated, Expression = "flag" });

        var result = DerivedValueCalculator.Compute(schema, new Dictionary<string, object?> { ["flag"] = true }, Evaluator);
        Assert.Equal("yes", result["label"]);
        Assert.Equal(true, result["score"]);
    }

    [Fact]
    public void Compute_coerces_date_type()
    {
        var dt = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);
        var schema = SchemaWith(
            new SchemaValue { Name = "when", Type = SchemaValueType.Date, Cadence = Cadence.Weekly },
            new SchemaValue { Name = "next", Type = SchemaValueType.Date, Cadence = Cadence.Weekly, Kind = SchemaValueKind.Calculated, Expression = "when" });

        var result = DerivedValueCalculator.Compute(schema, new Dictionary<string, object?> { ["when"] = dt }, Evaluator);
        Assert.Equal(dt, result["next"]);
    }

    [Fact]
    public void Compute_division_by_zero_yields_null()
    {
        var schema = SchemaWith(
            new SchemaValue { Name = "a", Type = SchemaValueType.Number, Cadence = Cadence.Weekly },
            new SchemaValue { Name = "bad", Type = SchemaValueType.Number, Cadence = Cadence.Weekly, Kind = SchemaValueKind.Calculated, Expression = "a / 0" });

        var result = DerivedValueCalculator.Compute(schema, new Dictionary<string, object?> { ["a"] = 1d }, Evaluator);
        Assert.Null(result["bad"]);
    }

    [Fact]
    public void Compute_cycle_among_calculated_values_yields_null()
    {
        var schema = SchemaWith(
            new SchemaValue { Name = "a", Type = SchemaValueType.Number, Cadence = Cadence.Weekly, Kind = SchemaValueKind.Calculated, Expression = "b + 1" },
            new SchemaValue { Name = "b", Type = SchemaValueType.Number, Cadence = Cadence.Weekly, Kind = SchemaValueKind.Calculated, Expression = "a + 1" });

        var result = DerivedValueCalculator.Compute(schema, new Dictionary<string, object?>(), Evaluator);
        Assert.Null(result["a"]);
        Assert.Null(result["b"]);
    }

    [Fact]
    public void Compute_self_reference_yields_null_without_throwing()
    {
        var schema = SchemaWith(
            new SchemaValue { Name = "a", Type = SchemaValueType.Number, Cadence = Cadence.Weekly, Kind = SchemaValueKind.Calculated, Expression = "a + 1" });

        var result = DerivedValueCalculator.Compute(schema, new Dictionary<string, object?>(), Evaluator);
        Assert.Null(result["a"]);
    }
}
