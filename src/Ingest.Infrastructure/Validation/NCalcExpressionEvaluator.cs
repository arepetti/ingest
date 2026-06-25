using System.Globalization;
using Ingest.Core.Abstractions;
using NCalc;
using NCalc.Handlers;

namespace Ingest.Infrastructure.Validation;

/// <summary>
/// NCalc-backed implementation of <see cref="IExpressionEvaluator"/>. Registers the date,
/// presence, and length helpers as case-insensitive functions (rather than variables) so they
/// can't clash with user-defined value names like <c>day</c> or <c>month</c>.
/// </summary>
public sealed class NCalcExpressionEvaluator : IExpressionEvaluator
{
    /// <summary>
    /// Built-in functions registered for every evaluation. Keys are case-insensitive.
    /// These don't depend on any external context; user-provided variables can therefore
    /// safely reuse names like <c>day</c>, <c>month</c>, etc. without clashing.
    /// </summary>
    private static readonly Dictionary<string, Func<object?[], object?>> BuiltIns =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["now"] = _ => DateTime.UtcNow,
            ["today"] = _ => DateTime.UtcNow.Date,
            ["dayOfWeek"] = args => (int)RequireDate(args, 0, "dayOfWeek").DayOfWeek,
            ["dayOfMonth"] = args => RequireDate(args, 0, "dayOfMonth").Day,
            ["dayOfYear"] = args => RequireDate(args, 0, "dayOfYear").DayOfYear,
            ["weekOfYear"] = args => ISOWeek.GetWeekOfYear(RequireDate(args, 0, "weekOfYear")),
            ["month"] = args => RequireDate(args, 0, "month").Month,
            ["year"] = args => RequireDate(args, 0, "year").Year,
            ["hour"] = args => RequireDate(args, 0, "hour").Hour,
            ["minute"] = args => RequireDate(args, 0, "minute").Minute,
            ["second"] = args => RequireDate(args, 0, "second").Second,
            ["isNull"] = args => args.Length == 0 || args[0] is null,
            ["coalesce"] = args =>
            {
                foreach (var a in args) if (a is not null) return a;
                return null;
            },
            ["len"] = args => args.Length == 0 ? 0 : args[0] switch
            {
                null => 0,
                string s => s.Length,
                System.Collections.ICollection c => c.Count,
                _ => throw new InvalidOperationException($"len() expects a string or collection, got {args[0]?.GetType().Name}."),
            },
            ["average"] = args =>
            {
                double sum = 0;
                var count = 0;
                foreach (var a in args)
                {
                    if (a is null) continue;
                    var n = ToAverageOperand(a);
                    sum += n;
                    count++;
                }
                return count == 0 ? null : sum / count;
            },
        };

    /// <inheritdoc />
    public ExpressionValidation EvaluateValidation(
        string expression,
        IReadOnlyDictionary<string, object?> parameters,
        IReadOnlyDictionary<string, Func<object?[], object?>>? customFunctions = null)
        => Interpret(Evaluate(expression, parameters, customFunctions));

    /// <inheritdoc />
    public object? Evaluate(
        string expression,
        IReadOnlyDictionary<string, object?> parameters,
        IReadOnlyDictionary<string, Func<object?[], object?>>? customFunctions = null)
    {
        // Newlines are stored verbatim so admins can format long expressions, but NCalc parses
        // the rule on a single line. Replace every CR/LF with a space — adjacent runs of
        // whitespace are harmless to the parser and keep token boundaries intact.
        var normalised = expression
            .Replace("\r\n", " ")
            .Replace('\n', ' ')
            .Replace('\r', ' ');

        var expr = new Expression(
            normalised,
            ExpressionOptions.IgnoreCaseAtBuiltInFunctions | ExpressionOptions.AllowNullParameter);

        foreach (var (k, v) in parameters)
            expr.Parameters[k] = v;

        // NCalc has no `null` literal in its grammar — bare `null` parses as an identifier and
        // would resolve to a parameter lookup. The docs (and several of our sample schemas) use
        // `null` extensively as the "no error" branch in `if(condition, 'msg', null)`, so we
        // expose it as a sentinel parameter. Registered AFTER user parameters so a value named
        // `null` (extremely unlikely, but legal) still can't shadow the literal.
        expr.Parameters["null"] = null;

        expr.EvaluateFunction += (name, args) =>
        {
            if (customFunctions is not null && customFunctions.TryGetValue(name, out var custom))
            {
                args.Result = custom(EvalArgs(args));
                return;
            }
            if (BuiltIns.TryGetValue(name, out var fn))
            {
                args.Result = fn(EvalArgs(args));
            }
        };

        return expr.Evaluate();
    }

    private static object?[] EvalArgs(FunctionArgs args)
    {
        var values = new object?[args.Parameters.Length];
        for (var i = 0; i < args.Parameters.Length; i++)
            values[i] = args.Parameters[i].Evaluate();
        return values;
    }

    private static double ToAverageOperand(object a) => a switch
    {
        bool b => b ? 1.0 : 0.0,
        double d when !double.IsNaN(d) => d,
        float f when !float.IsNaN(f) => f,
        int i => i,
        long l => l,
        decimal m => (double)m,
        string s when double.TryParse(s, System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out var p) => p,
        _ => throw new InvalidOperationException($"average() expects numeric or boolean arguments, got {a.GetType().Name}."),
    };

    private static DateTime RequireDate(object?[] args, int index, string fn)
    {
        if (args.Length <= index || args[index] is null)
            throw new InvalidOperationException($"{fn}() requires a date argument.");
        return args[index] switch
        {
            DateTime dt => dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt,
            DateTimeOffset dto => dto.UtcDateTime,
            string s when DateTime.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) => parsed,
            _ => throw new InvalidOperationException($"{fn}() expects a date, got {args[index]?.GetType().Name}."),
        };
    }

    private static ExpressionValidation Interpret(object? result) => result switch
    {
        null => ExpressionValidation.Valid,
        bool b => b ? ExpressionValidation.Valid : ExpressionValidation.Invalid(),
        string s => string.IsNullOrWhiteSpace(s) ? ExpressionValidation.Valid : ExpressionValidation.Invalid(s),
        _ => ExpressionValidation.Invalid($"Expression returned an unexpected result: {result}"),
    };
}
