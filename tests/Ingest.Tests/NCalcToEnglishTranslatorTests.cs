using Ingest.Infrastructure.Validation;

namespace Ingest.Tests;

public class NCalcToEnglishTranslatorTests
{
    private static string English(string expr, IReadOnlyDictionary<string, string>? labels = null) =>
        new NCalcTranslator().TranslateToEnglish(expr, labels);

    [Fact]
    public void Comparison_reads_as_words()
    {
        var text = English("value > 0");
        Assert.Equal("value is greater than 0", text);
    }

    [Theory]
    [InlineData("a >= b", "is greater than or equal to")]
    [InlineData("a < b", "is less than")]
    [InlineData("a <= b", "is less than or equal to")]
    [InlineData("a == b", "is equal to")]
    [InlineData("a != b", "is not equal to")]
    [InlineData("a + b", "plus")]
    [InlineData("a - b", "minus")]
    [InlineData("a * b", "times")]
    [InlineData("a / b", "divided by")]
    public void Binary_operators_map_to_phrases(string expr, string phrase)
    {
        Assert.Contains(phrase, English(expr));
    }

    [Fact]
    public void And_or_read_naturally()
    {
        Assert.Contains(" and ", English("a > 0 and b > 0"));
        Assert.Contains(" or ", English("a > 0 or b > 0"));
    }

    [Fact]
    public void If_reads_as_if_then_otherwise()
    {
        var text = English("if(value > 0, 'ok', 'no')");
        Assert.Contains("if ", text);
        Assert.Contains("then", text);
        Assert.Contains("otherwise", text);
    }

    [Fact]
    public void In_reads_as_is_one_of()
    {
        var text = English("value in ('a', 'b', 'c')");
        Assert.Contains("is one of", text);
        Assert.Contains("\"a\"", text);
    }

    [Fact]
    public void IsNull_reads_as_is_empty()
    {
        Assert.Contains("is empty", English("isNull(other)"));
    }

    [Fact]
    public void Len_reads_as_the_length_of()
    {
        Assert.Contains("the length of", English("len(code) > 3"));
    }

    [Fact]
    public void Outer_parentheses_are_stripped()
    {
        // A single top-level binary shouldn't come back wrapped in parentheses.
        Assert.Equal("value is greater than 0", English("value > 0"));
    }

    [Fact]
    public void Identifiers_use_their_label_when_supplied()
    {
        var text = English("score > 10", new Dictionary<string, string> { ["score"] = "Overall score" });
        Assert.Contains("Overall score is greater than 10", text);
    }

    [Fact]
    public void Bound_keys_expand_to_the_maximum_of()
    {
        var text = English("[tonnes.maximum] - tonnes");
        Assert.Contains("the maximum of tonnes", text);
    }

    [Fact]
    public void Newlines_are_stripped_before_parsing()
    {
        var text = English("if(\n  value > 0,\n  'ok',\n  'no')");
        Assert.Contains("value is greater than 0", text);
    }
}
