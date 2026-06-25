using Ingest.Core.Analytics;

namespace Ingest.Tests;

/// <summary>
/// Tests for <see cref="AnomalyDetector.Score"/>: the small z-score / MAD outlier detector behind
/// the Explore page's anomaly highlighting and board.
/// </summary>
public class AnomalyDetectorTests
{
    [Fact]
    public void Too_little_history_yields_no_score()
    {
        var history = new double[] { 10, 11, 12 }; // < MinHistory (4)
        var (z, isAnomaly) = AnomalyDetector.Score(history, 100, threshold: 2.5, robust: false);
        Assert.Null(z);
        Assert.False(isAnomaly);
    }

    [Fact]
    public void Flat_history_is_never_anomalous()
    {
        var history = new double[] { 50, 50, 50, 50, 50 };
        var (z, isAnomaly) = AnomalyDetector.Score(history, 9999, threshold: 2.5, robust: false);
        Assert.Equal(0d, z);
        Assert.False(isAnomaly);
    }

    [Fact]
    public void Clear_outlier_is_flagged_with_a_large_score()
    {
        var history = new double[] { 98, 101, 99, 102, 100 };
        var (z, isAnomaly) = AnomalyDetector.Score(history, 1000, threshold: 2.5, robust: false);
        Assert.NotNull(z);
        Assert.True(z!.Value > 2.5);
        Assert.True(isAnomaly);
    }

    [Fact]
    public void Value_in_line_with_history_is_not_flagged()
    {
        var history = new double[] { 98, 101, 99, 102, 100 };
        var (z, isAnomaly) = AnomalyDetector.Score(history, 100.5, threshold: 2.5, robust: false);
        Assert.False(isAnomaly);
        Assert.True(Math.Abs(z!.Value) < 2.5);
    }

    [Fact]
    public void Negative_deviation_is_flagged_too()
    {
        var history = new double[] { 100, 100, 100, 100, 100, 99, 101 };
        var (z, isAnomaly) = AnomalyDetector.Score(history, 0, threshold: 2.5, robust: false);
        Assert.True(z!.Value < 0);
        Assert.True(isAnomaly);
    }

    [Fact]
    public void Robust_mode_resists_a_single_spike_poisoning_the_baseline()
    {
        // One huge spike already sits in the history. Under mean/SD that inflates the spread so the
        // next moderate outlier hides; the robust median/MAD baseline still catches it.
        var history = new double[] { 100, 101, 99, 100, 102, 5000 };
        const double current = 200;

        var standard = AnomalyDetector.Score(history, current, threshold: 2.5, robust: false);
        var robust = AnomalyDetector.Score(history, current, threshold: 2.5, robust: true);

        Assert.False(standard.IsAnomaly);
        Assert.True(robust.IsAnomaly);
    }

    [Fact]
    public void Window_and_threshold_clamp_to_supported_ranges()
    {
        Assert.Equal(AnomalyDetector.MinWindow, AnomalyDetector.ClampWindow(1));
        Assert.Equal(AnomalyDetector.MaxWindow, AnomalyDetector.ClampWindow(10_000));
        Assert.Equal(AnomalyDetector.MinThreshold, AnomalyDetector.ClampThreshold(0.1));
        Assert.Equal(AnomalyDetector.MaxThreshold, AnomalyDetector.ClampThreshold(99));
    }
}
