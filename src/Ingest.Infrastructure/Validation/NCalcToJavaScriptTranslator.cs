using System.Globalization;
using System.Text.Json;
using System.Threading;
using Ingest.Core.Abstractions;
using NCalc.Domain;
using NCalc.Factories;
using NCalc.Visitors;

namespace Ingest.Infrastructure.Validation;

/// <summary>
/// Default <see cref="IExpressionTranslator"/>. Reuses NCalc's parser to build the AST, then
/// walks it with a <see cref="ILogicalExpressionVisitor{T}"/> that emits an equivalent
/// JavaScript expression. The visitor preserves short-circuit semantics for
/// <c>and</c>/<c>or</c> and <c>if(c, a, b)</c>, and routes every other operation through a
/// small helper namespace (<c>H</c>) so the runtime can encode the same null-handling rules
/// the .NET evaluator uses.
/// </summary>
public sealed class NCalcToJavaScriptTranslator : IExpressionTranslator
{
    private readonly ILogicalExpressionFactory _factory = LogicalExpressionFactory.GetInstance();

    /// <inheritdoc />
    public JsExpressionTranslation TranslateToJavaScript(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new ArgumentException("Expression must not be empty.", nameof(expression));

        // Mirror the runtime normalisation done by NCalcExpressionEvaluator: newlines become
        // spaces so multi-line rules parse the same way they evaluate.
        var normalised = Normalise(expression);

        var tree = _factory.Create(normalised, NCalc.ExpressionOptions.IgnoreCaseAtBuiltInFunctions, CancellationToken.None);
        var visitor = new JsEmittingVisitor();
        var js = tree.Accept(visitor, CancellationToken.None);

        return new JsExpressionTranslation(js, visitor.Identifiers, visitor.Functions);
    }

    /// <inheritdoc />
    public ExpressionSyntaxResult ValidateSyntax(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new ArgumentException("Expression must not be empty.", nameof(expression));

        var normalised = Normalise(expression);
        try
        {
            // We deliberately only build the AST. Identifier/function lookups happen at
            // evaluation time, so unknown names won't trip this check — that's by design: full
            // validation runs when the schema is saved.
            _ = _factory.Create(normalised, NCalc.ExpressionOptions.IgnoreCaseAtBuiltInFunctions, CancellationToken.None);
            return new ExpressionSyntaxResult(true);
        }
        catch (Exception ex)
        {
            return new ExpressionSyntaxResult(false, ex.Message);
        }
    }

    private static string Normalise(string expression) =>
        expression.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');

    private sealed class JsEmittingVisitor : ILogicalExpressionVisitor<string>
    {
        private readonly HashSet<string> _identifiers = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _functions = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> Identifiers => _identifiers.OrderBy(x => x, StringComparer.Ordinal).ToList();
        public IReadOnlyList<string> Functions => _functions.OrderBy(x => x, StringComparer.Ordinal).ToList();

        public string Visit(TernaryExpression expression, CancellationToken ct)
        {
            // NCalc's TernaryExpression is the `c ? a : b` operator. We translate it to a JS
            // ternary so both branches preserve laziness.
            var c = expression.LeftExpression.Accept(this, ct);
            var a = expression.MiddleExpression.Accept(this, ct);
            var b = expression.RightExpression.Accept(this, ct);
            return $"(H.bool({c}) ? ({a}) : ({b}))";
        }

        public string Visit(BinaryExpression expression, CancellationToken ct)
        {
            var ct2 = ct;
            // Short-circuit operators must emit JS that doesn't evaluate the right side when
            // the left side already settles the result. Plain `H.and(left, right)` would
            // evaluate both eagerly.
            if (expression.Type == BinaryExpressionType.And)
            {
                var l = expression.LeftExpression.Accept(this, ct2);
                var r = expression.RightExpression.Accept(this, ct2);
                return $"(H.bool({l}) ? H.bool({r}) : false)";
            }
            if (expression.Type == BinaryExpressionType.Or)
            {
                var l = expression.LeftExpression.Accept(this, ct2);
                var r = expression.RightExpression.Accept(this, ct2);
                return $"(H.bool({l}) ? true : H.bool({r}))";
            }

            // `value in (a, b, c)` is parsed as a binary with a LogicalExpressionList on the
            // right. Render it through a helper, expanding the list inline so we don't have
            // to teach the runtime about NCalc's list semantics.
            if (expression.Type == BinaryExpressionType.In || expression.Type == BinaryExpressionType.NotIn)
            {
                var v = expression.LeftExpression.Accept(this, ct2);
                var args = expression.RightExpression switch
                {
                    LogicalExpressionList list => string.Join(", ", list.Select(item => item.Accept(this, ct2))),
                    _ => expression.RightExpression.Accept(this, ct2),
                };
                var prefix = expression.Type == BinaryExpressionType.NotIn ? "!" : "";
                return $"({prefix}H.in({v}, [{args}]))";
            }

            // Everything else goes through H.<op>(left, right) where the helper applies the
            // same null/coercion rules the .NET evaluator does.
            var op = BinaryOpHelperName(expression.Type);
            var leftJs = expression.LeftExpression.Accept(this, ct2);
            var rightJs = expression.RightExpression.Accept(this, ct2);
            return $"H.{op}({leftJs}, {rightJs})";
        }

        public string Visit(UnaryExpression expression, CancellationToken ct)
        {
            var inner = expression.Expression.Accept(this, ct);
            return expression.Type switch
            {
                UnaryExpressionType.Not => $"(!H.bool({inner}))",
                UnaryExpressionType.Negate => $"H.neg({inner})",
                UnaryExpressionType.Positive => $"({inner})",
                UnaryExpressionType.BitwiseNot => $"H.bitNot({inner})",
                UnaryExpressionType.Factorial => $"H.fact({inner})",
                _ => throw new NotSupportedException($"Unsupported unary operator '{expression.Type}'."),
            };
        }

        public string Visit(ValueExpression expression, CancellationToken ct)
        {
            return expression.Value switch
            {
                null => "null",
                bool b => b ? "true" : "false",
                string s => JsonSerializer.Serialize(s),
                char c => JsonSerializer.Serialize(c.ToString()),
                int i => i.ToString(CultureInfo.InvariantCulture),
                long l => l.ToString(CultureInfo.InvariantCulture),
                double d => FormatDouble(d),
                float f => FormatDouble(f),
                decimal m => m.ToString(CultureInfo.InvariantCulture),
                DateTime dt => $"H.date({JsonSerializer.Serialize(dt.ToString("o", CultureInfo.InvariantCulture))})",
                Guid g => JsonSerializer.Serialize(g.ToString()),
                _ => JsonSerializer.Serialize(expression.Value.ToString() ?? ""),
            };
        }

        public string Visit(Function expression, CancellationToken ct)
        {
            var ct2 = ct;
            var name = expression.Identifier.Name;
            var lower = name.ToLowerInvariant();
            _functions.Add(name);

            // `if(c, a, b)` is special — needs JS ternary semantics to short-circuit. Anything
            // else flows through the helper layer where the runtime can implement it once.
            if (lower == "if")
            {
                if (expression.Parameters.Count != 3)
                    throw new InvalidOperationException("if() expects 3 arguments.");
                var c = expression.Parameters[0].Accept(this, ct2);
                var a = expression.Parameters[1].Accept(this, ct2);
                var b = expression.Parameters[2].Accept(this, ct2);
                return $"(H.bool({c}) ? ({a}) : ({b}))";
            }

            var argList = string.Join(", ", expression.Parameters.Select(p => p.Accept(this, ct2)));
            // Function names are looked up case-insensitively at runtime, but we keep the
            // user's spelling here so error messages match what they wrote.
            return $"H.call({JsonSerializer.Serialize(name)}, [{argList}])";
        }

        public string Visit(Identifier expression, CancellationToken ct)
        {
            // `null` isn't a real NCalc literal — the runtime evaluator exposes it as a sentinel
            // parameter — but in JavaScript it IS a literal, so we emit it directly. Treating it
            // as an identifier would force the runtime helper into a lookup against a key the
            // emitted variables bag is never expected to carry.
            if (string.Equals(expression.Name, "null", StringComparison.OrdinalIgnoreCase))
                return "null";

            _identifiers.Add(expression.Name);
            // Bracket notation + JSON-encoded name keeps the emitted code safe from prototype
            // pollution and accidental keyword clashes.
            return $"H.var(V, {JsonSerializer.Serialize(expression.Name)})";
        }

        public string Visit(LogicalExpressionList expression, CancellationToken ct)
        {
            // Hit when a list shows up outside an explicit context (rare). Render as a JS
            // array; consumers can decide what to do with it.
            return "[" + string.Join(", ", expression.Select(e => e.Accept(this, ct))) + "]";
        }

        private static string BinaryOpHelperName(BinaryExpressionType type) => type switch
        {
            BinaryExpressionType.Plus => "add",
            BinaryExpressionType.Minus => "sub",
            BinaryExpressionType.Times => "mul",
            BinaryExpressionType.Div => "div",
            BinaryExpressionType.Modulo => "mod",
            BinaryExpressionType.Equal => "eq",
            BinaryExpressionType.NotEqual => "neq",
            BinaryExpressionType.Greater => "gt",
            BinaryExpressionType.GreaterOrEqual => "gte",
            BinaryExpressionType.Lesser => "lt",
            BinaryExpressionType.LesserOrEqual => "lte",
            BinaryExpressionType.Exponentiation => "pow",
            BinaryExpressionType.BitwiseAnd => "bitAnd",
            BinaryExpressionType.BitwiseOr => "bitOr",
            BinaryExpressionType.BitwiseXOr => "bitXor",
            BinaryExpressionType.LeftShift => "shl",
            BinaryExpressionType.RightShift => "shr",
            BinaryExpressionType.Like => "like",
            BinaryExpressionType.NotLike => "notLike",
            _ => throw new NotSupportedException($"Unsupported binary operator '{type}'."),
        };

        private static string FormatDouble(double d)
        {
            // "R" gives a round-trip representation so the JS literal evaluates to the same
            // double. JS doesn't grok "NaN"/"Infinity" as bare tokens so route them through a
            // helper for symmetry — but they shouldn't show up in validation rules anyway.
            if (double.IsNaN(d)) return "(0/0)";
            if (double.IsPositiveInfinity(d)) return "(1/0)";
            if (double.IsNegativeInfinity(d)) return "(-1/0)";
            return d.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
