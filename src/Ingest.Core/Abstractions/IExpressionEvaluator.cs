namespace Ingest.Core.Abstractions;

/// <summary>
/// Result of evaluating a validation expression. A null/whitespace <see cref="ErrorMessage"/>
/// alongside <c>IsValid == false</c> means "invalid without a specific message" — the caller is
/// expected to fall back to a generic phrasing in that case.
/// </summary>
/// <param name="IsValid">Whether the expression considered the input valid.</param>
/// <param name="ErrorMessage">Optional human-readable explanation surfaced to API callers when invalid.</param>
public sealed record ExpressionValidation(bool IsValid, string? ErrorMessage = null)
{
    /// <summary>Cached "valid" result; identity-comparable so hot paths can avoid allocation.</summary>
    public static readonly ExpressionValidation Valid = new(true, null);

    /// <summary>Build an "invalid" result with an optional caller-facing message.</summary>
    /// <param name="message">Optional explanation; null/empty produces a generic invalid.</param>
    /// <returns>A new <see cref="ExpressionValidation"/> with <c>IsValid = false</c>.</returns>
    public static ExpressionValidation Invalid(string? message = null) => new(false, message);
}

/// <summary>
/// Adapter over a scripting engine (currently NCalc) used to run schema- and value-level
/// validation rules. Authors of schemas can express constraints as short expressions in YAML/JSON
/// instead of going through code, and the engine interprets the return value as a validation
/// outcome.
/// </summary>
public interface IExpressionEvaluator
{
    /// <summary>
    /// Evaluate an NCalc expression and interpret its result as a validation outcome:
    /// <list type="bullet">
    ///   <item><c>true</c>, <c>null</c>, or an empty/whitespace string =&gt; valid;</item>
    ///   <item><c>false</c> =&gt; invalid (no specific message);</item>
    ///   <item>non-empty string =&gt; invalid, with the string used as the error message;</item>
    ///   <item>anything else =&gt; treated as invalid with a generic message.</item>
    /// </list>
    /// </summary>
    /// <param name="expression">NCalc expression text.</param>
    /// <param name="parameters">Named variables exposed to the expression.</param>
    /// <param name="customFunctions">
    /// Optional caller-defined functions. Keys are case-insensitive function names; values are
    /// the handlers. These are evaluated alongside the built-in functions registered by the
    /// concrete evaluator.
    /// </param>
    /// <returns>The validation outcome derived from the expression's result.</returns>
    ExpressionValidation EvaluateValidation(
        string expression,
        IReadOnlyDictionary<string, object?> parameters,
        IReadOnlyDictionary<string, Func<object?[], object?>>? customFunctions = null);

    /// <summary>
    /// Evaluate an expression and return its raw result, without the validation-shaped
    /// reinterpretation. Used by rules that need to distinguish <c>true</c> from <c>null</c>
    /// or <c>""</c> — most notably the per-value Warning rule, which fires on
    /// truthy / non-empty results.
    /// </summary>
    /// <param name="expression">Expression text.</param>
    /// <param name="parameters">Named variables exposed to the expression.</param>
    /// <param name="customFunctions">Optional caller-defined functions.</param>
    /// <returns>The raw value the expression produced (<c>bool</c>, <c>string</c>, number, <c>null</c>, …).</returns>
    object? Evaluate(
        string expression,
        IReadOnlyDictionary<string, object?> parameters,
        IReadOnlyDictionary<string, Func<object?[], object?>>? customFunctions = null);
}
