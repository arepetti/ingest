using Ingest.Core.Common;

namespace Ingest.Core.Abstractions;

/// <summary>
/// Translates a validation expression authored in the server-side rule language into an
/// equivalent JavaScript fragment that the admin SPA can execute client-side. Used to power
/// live "Enabled if" / "Visible if" / "Warning" feedback in the submission editor without
/// duplicating the parser in two languages.
/// </summary>
/// <remarks>
/// The translated JavaScript is meant to be wrapped in a small runtime: a variables object
/// (<c>V</c>) and a helpers object (<c>H</c>) are expected to be in scope when it runs.
/// Identifiers in the source expression are emitted as case-insensitive lookups against
/// <c>V</c>; built-in and user-defined function calls go through <c>H</c>. The resulting
/// fragment is always a single expression (no statements) and is safe to wrap with
/// <c>new Function("V", "H", "return (" + js + ")")</c>: every identifier and function name
/// is embedded as a JSON-encoded string literal, so no arbitrary JavaScript can leak through.
/// </remarks>
public interface IExpressionTranslator
{
    /// <summary>
    /// Parse <paramref name="expression"/> with the same engine used by the validator and
    /// emit an equivalent JavaScript fragment.
    /// </summary>
    /// <param name="expression">The validation expression source.</param>
    /// <returns>The translation result (JS fragment plus the identifiers/functions it references).</returns>
    /// <exception cref="System.ArgumentException">The expression is null/empty/whitespace.</exception>
    /// <exception cref="System.Exception">The expression failed to parse — propagates the underlying NCalc parser exception.</exception>
    JsExpressionTranslation TranslateToJavaScript(string expression);

    /// <summary>
    /// Run the parser without emitting any code and report whether the expression is
    /// syntactically well-formed. This is the cheap "live as you type" check the schema editor
    /// uses to surface red squiggles on validators; it deliberately does not flag unknown
    /// identifiers (full validation runs server-side when the schema is saved).
    /// </summary>
    /// <param name="expression">The validation expression source.</param>
    /// <returns>The validation outcome: <see cref="ExpressionSyntaxResult.Ok"/> set to <c>true</c> when the parser accepted the input.</returns>
    /// <exception cref="System.ArgumentException">The expression is null/empty/whitespace — distinct from a syntax error, this is a caller bug.</exception>
    ExpressionSyntaxResult ValidateSyntax(string expression);

    /// <summary>
    /// Parse <paramref name="expression"/> with the same engine used by the validator and render
    /// it as a plain-English sentence. Uses the same AST walk as
    /// <see cref="TranslateToJavaScript"/>, so the two stay in lock-step. Backs the human-readable
    /// rule explanations in the schema PDF export (and the reserved <c>text/plain</c> target of the
    /// translate endpoint).
    /// </summary>
    /// <param name="expression">The validation expression source.</param>
    /// <param name="valueLabels">
    /// Optional map from a value's machine name to its friendly label. When supplied, identifiers
    /// (and their <c>[name.minimum]</c> / <c>[name.maximum]</c> bound keys) are rendered using the
    /// label instead of the raw name. Case-insensitive lookups are expected.
    /// </param>
    /// <returns>A human-readable description of the rule.</returns>
    /// <exception cref="System.ArgumentException">The expression is null/empty/whitespace.</exception>
    /// <exception cref="System.Exception">The expression failed to parse — propagates the underlying NCalc parser exception.</exception>
    string TranslateToEnglish(string expression, IReadOnlyDictionary<string, string>? valueLabels = null);
}

/// <summary>Outcome of a syntax-only check via <see cref="IExpressionTranslator.ValidateSyntax"/>.</summary>
/// <param name="Ok">True when the parser accepted the expression. When false, <paramref name="Error"/> is non-null.</param>
/// <param name="Error">Parser error message; <c>null</c> on success.</param>
/// <param name="Position">Optional 0-based character offset the parser stumbled at; <c>null</c> when the underlying parser doesn't expose one.</param>
public sealed record ExpressionSyntaxResult(bool Ok, string? Error = null, int? Position = null)
{
    /// <summary>Structured counterpart to <see cref="Error"/>.</summary>
    public Diagnostic? ErrorDetail { get; init; } =
        Error is null
            ? null
            : Diagnostic.Create(
                DiagnosticCodes.Expressions.ParseFailed,
                Error,
                ("position", Position));
}

/// <summary>Outcome of an expression-to-JavaScript translation.</summary>
/// <param name="Js">A JavaScript expression (not a statement) that, when evaluated with <c>V</c> and <c>H</c> in scope, returns the same result as the source rule.</param>
/// <param name="Identifiers">Names of variables the expression reads from. Useful to hint the client at what needs to be in the context.</param>
/// <param name="Functions">Names of functions the expression calls. Useful to validate that the client's helpers can serve them all.</param>
public sealed record JsExpressionTranslation(
    string Js,
    IReadOnlyList<string> Identifiers,
    IReadOnlyList<string> Functions);
