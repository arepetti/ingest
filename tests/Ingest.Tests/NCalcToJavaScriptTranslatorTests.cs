using Ingest.Infrastructure.Validation;

namespace Ingest.Tests;

public class NCalcToJavaScriptTranslatorTests
{
    private static string Translate(string expr) =>
        new NCalcToJavaScriptTranslator().TranslateToJavaScript(expr).Js;

    [Fact]
    public void Comparison_routes_through_helper()
    {
        // The helper handles null/coercion the same way the .NET evaluator does, so all
        // arithmetic/comparison binaries must go through it (no bare JS operators).
        var js = Translate("value > 0");
        Assert.Contains("H.gt(", js);
        Assert.Contains("H.var(V, \"value\")", js);
    }

    [Fact]
    public void And_or_short_circuit_via_ternary()
    {
        // Plain `H.and(left, right)` would evaluate both sides eagerly; the translator must
        // emit a ternary so the right side isn't touched when the left already settles it.
        var js = Translate("a and b");
        Assert.Contains("H.bool(", js);
        Assert.Contains("? H.bool(", js);
    }

    [Fact]
    public void If_function_uses_native_ternary()
    {
        // if(c, a, b) must short-circuit just like the And/Or operators — we rely on the
        // ternary so the unused branch is never evaluated.
        var js = Translate("if(value > 0, 'ok', null)");
        Assert.Contains("?", js);
        Assert.Contains(":", js);
        Assert.DoesNotContain("H.call(\"if\"", js);
    }

    [Fact]
    public void Strings_are_safely_encoded()
    {
        // Embedding a string literal through the JSON serialiser protects against any quote /
        // newline / unicode in user-authored rules sneaking through as raw JS. The default
        // encoder escapes `"` as `\u0022`, which is functionally identical to `\"` in JS but
        // also safe for inlining inside HTML/script blocks.
        var js = Translate("if(value == 'has \"quotes\"', 1, 0)");
        Assert.DoesNotContain("has \"quotes\"", js); // raw text must not leak through
        Assert.Contains("\\u0022", js);              // quotes were escaped to a JSON-safe form
    }

    [Fact]
    public void In_binary_expands_to_helper_call_with_array()
    {
        var js = Translate("value in ('a', 'b', 'c')");
        Assert.Contains("H.in(", js);
        Assert.Contains("[\"a\", \"b\", \"c\"]", js);
    }

    [Fact]
    public void Newlines_are_stripped_before_parsing()
    {
        // Multi-line authoring stores newlines verbatim but the translator (like the
        // evaluator) folds them into spaces so the same source parses both places.
        var js = Translate("if(\n  value > 0,\n  'ok',\n  null)");
        Assert.Contains("H.gt(", js);
    }

    [Fact]
    public void Tracks_identifiers_and_functions()
    {
        var t = new NCalcToJavaScriptTranslator()
            .TranslateToJavaScript("if(not isNull(other), other + value, 0)");
        Assert.Contains("other", t.Identifiers);
        Assert.Contains("value", t.Identifiers);
        Assert.Contains("isNull", t.Functions);
    }

    [Fact]
    public void Bracketed_identifier_with_dot_becomes_a_runtime_var_lookup()
    {
        // The bound-namespace keys (`[name.minimum]` / `[name.maximum]`) survive translation
        // as plain runtime lookups — `H.var` does a case-insensitive scan of the variables
        // bag so the dotted key matches the parameter we register on both sides.
        var t = new NCalcToJavaScriptTranslator()
            .TranslateToJavaScript("[tonnes_collected.maximum] - tonnes_collected");
        Assert.Contains("H.var(V, \"tonnes_collected.maximum\")", t.Js);
        Assert.Contains("tonnes_collected.maximum", t.Identifiers);
        Assert.Contains("tonnes_collected", t.Identifiers);
    }
}
