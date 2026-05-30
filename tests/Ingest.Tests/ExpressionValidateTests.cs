using Ingest.Infrastructure.Validation;

namespace Ingest.Tests;

/// <summary>
/// Syntax-only validation tests for <see cref="NCalcToJavaScriptTranslator.ValidateSyntax"/>.
/// These deliberately do <em>not</em> probe unknown identifiers — that's a runtime concern, full
/// schema validation runs at save-time.
/// </summary>
public class ExpressionValidateTests
{
    [Fact]
    public void Valid_expression_returns_ok()
    {
        var t = new NCalcToJavaScriptTranslator();
        var r = t.ValidateSyntax("value > 0 and value < 100");
        Assert.True(r.Ok);
        Assert.Null(r.Error);
    }

    [Fact]
    public void Broken_expression_returns_not_ok_with_error_message()
    {
        var t = new NCalcToJavaScriptTranslator();
        var r = t.ValidateSyntax("1 + ");
        Assert.False(r.Ok);
        Assert.False(string.IsNullOrWhiteSpace(r.Error));
    }

    [Fact]
    public void Unknown_identifier_is_not_flagged_at_syntax_time()
    {
        // The whole point of the endpoint: "syntax only" means unresolved names sail through.
        // Full validation (and any "did you mean…?" reporting) happens server-side on save.
        var t = new NCalcToJavaScriptTranslator();
        var r = t.ValidateSyntax("foo + bar * baz");
        Assert.True(r.Ok);
    }

    [Fact]
    public void Multiline_expression_is_normalised_like_translation()
    {
        // Multi-line authoring is the whole reason the textarea exists; the parser is fed a
        // single-line normalised form just like the translator.
        var t = new NCalcToJavaScriptTranslator();
        var r = t.ValidateSyntax("value > 0\n  and\n  value < 100");
        Assert.True(r.Ok);
    }

    [Fact]
    public void Empty_input_throws_argument_exception()
    {
        var t = new NCalcToJavaScriptTranslator();
        Assert.Throws<ArgumentException>(() => t.ValidateSyntax("   "));
    }
}
