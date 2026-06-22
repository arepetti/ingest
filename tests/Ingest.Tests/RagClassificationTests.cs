using Ingest.Core.Entities;

namespace Ingest.Tests;

/// <summary>
/// Unit tests for <see cref="SchemaValue.ClassifyRag"/> — the green/amber/red bucketing that must
/// stay in lock-step with how the target band is drawn on the Explore/historical charts.
/// </summary>
public class RagClassificationTests
{
    private static SchemaValue Banded(double? amberMin, double? greenMin, double? greenMax, double? amberMax) => new()
    {
        Name = "v",
        Type = SchemaValueType.Number,
        AmberMin = amberMin,
        GreenMin = greenMin,
        GreenMax = greenMax,
        AmberMax = amberMax,
    };

    [Fact]
    public void No_band_has_no_classification()
    {
        var v = new SchemaValue { Name = "v", Type = SchemaValueType.Number };
        Assert.False(v.HasTargetBand);
        Assert.Null(v.ClassifyRag(42));
    }

    [Theory]
    // Full band: amber [0,100], green [50,80].
    [InlineData(-1, RagStatus.Red)]
    [InlineData(0, RagStatus.Amber)]   // on the acceptable floor, below the ideal range
    [InlineData(49, RagStatus.Amber)]
    [InlineData(50, RagStatus.Green)]  // ideal lower edge is inclusive
    [InlineData(65, RagStatus.Green)]
    [InlineData(80, RagStatus.Green)]  // ideal upper edge is inclusive
    [InlineData(81, RagStatus.Amber)]
    [InlineData(100, RagStatus.Amber)] // on the acceptable ceiling
    [InlineData(101, RagStatus.Red)]
    public void Full_band_classifies_by_zone(double value, RagStatus expected)
    {
        Assert.Equal(expected, Banded(0, 50, 80, 100).ClassifyRag(value));
    }

    [Theory]
    // Acceptable-only band [0,100]: inside is amber (no ideal range defined), outside is red.
    [InlineData(-1, RagStatus.Red)]
    [InlineData(0, RagStatus.Amber)]
    [InlineData(50, RagStatus.Amber)]
    [InlineData(100, RagStatus.Amber)]
    [InlineData(101, RagStatus.Red)]
    public void Amber_only_band_is_amber_inside(double value, RagStatus expected)
    {
        Assert.Equal(expected, Banded(0, null, null, 100).ClassifyRag(value));
    }

    [Theory]
    // One-sided "lower is better": green max 5, amber max 10, no minimums.
    [InlineData(-1000, RagStatus.Green)] // green has no lower edge, so it reaches down forever
    [InlineData(5, RagStatus.Green)]
    [InlineData(6, RagStatus.Amber)]
    [InlineData(10, RagStatus.Amber)]
    [InlineData(11, RagStatus.Red)]
    public void One_sided_band_classifies_by_upper_edges(double value, RagStatus expected)
    {
        Assert.Equal(expected, Banded(null, null, 5, 10).ClassifyRag(value));
    }
}
