namespace Ingest.Core.Analytics;

/// <summary>
/// A small, dependency-free statistical outlier detector used by the Explore page. Given a value
/// and the recent history that precedes it, it returns a standardised score and whether that score
/// crosses a threshold. Two methods are supported: classic mean/standard-deviation z-scores and a
/// robust median/MAD variant that copes better with spiky KPI series.
/// </summary>
/// <remarks>
/// This is deliberately a <em>view aid</em>, not a stored validation result: it never rejects a
/// submission and never writes anything. Hard, history-based rejections belong in a schema
/// <c>Warning</c>/validation rule (<c>previous()</c> / <c>latest()</c>), which run in the submission
/// pipeline. See <c>docs/admin-user-guide/explore.md</c>.
/// </remarks>
public static class AnomalyDetector
{
    /// <summary>
    /// The fewest preceding values required before a score can be computed. With less history the
    /// baseline is meaningless, so the point is reported as not-anomalous with a <c>null</c> score.
    /// </summary>
    public const int MinHistory = 4;

    /// <summary>Smallest accepted rolling-window size (buckets of preceding history).</summary>
    public const int MinWindow = MinHistory;

    /// <summary>Largest accepted rolling-window size — caps work and keeps the baseline "recent".</summary>
    public const int MaxWindow = 104;

    /// <summary>Smallest accepted <c>|z|</c> cutoff.</summary>
    public const double MinThreshold = 1.0;

    /// <summary>Largest accepted <c>|z|</c> cutoff.</summary>
    public const double MaxThreshold = 5.0;

    /// <summary>Default rolling-window size when the caller doesn't specify one.</summary>
    public const int DefaultWindow = 12;

    /// <summary>Default <c>|z|</c> cutoff when the caller doesn't specify one.</summary>
    public const double DefaultThreshold = 2.5;

    /// <summary>Clamp a requested window into the supported range.</summary>
    public static int ClampWindow(int window) => Math.Clamp(window, MinWindow, MaxWindow);

    /// <summary>Clamp a requested threshold into the supported range.</summary>
    public static double ClampThreshold(double threshold) => Math.Clamp(threshold, MinThreshold, MaxThreshold);

    /// <summary>
    /// Score <paramref name="current"/> against the values that precede it.
    /// </summary>
    /// <param name="history">
    /// The values strictly before the point under test, already trimmed to the rolling window. Order
    /// doesn't matter. Periods with no data should simply be absent (a gap is not a zero).
    /// </param>
    /// <param name="current">The value being tested.</param>
    /// <param name="threshold">The <c>|z|</c> cutoff at or above which the point is an anomaly.</param>
    /// <param name="robust">When <c>true</c>, use median + MAD instead of mean + standard deviation.</param>
    /// <returns>
    /// The standardised score and the anomaly flag. <c>Z</c> is <c>null</c> (and the flag
    /// <c>false</c>) when there isn't enough history; a flat history (zero spread) scores <c>0</c>
    /// and is never anomalous.
    /// </returns>
    public static (double? Z, bool IsAnomaly) Score(
        IReadOnlyList<double> history, double current, double threshold, bool robust)
    {
        if (history is null || history.Count < MinHistory) return (null, false);

        double z;
        if (robust)
        {
            var median = Median(history);
            // Median Absolute Deviation, scaled by 0.6745 so it estimates the standard deviation for
            // normally-distributed data — making the threshold comparable to a plain z-score.
            var deviations = new double[history.Count];
            for (var i = 0; i < history.Count; i++) deviations[i] = Math.Abs(history[i] - median);
            var mad = Median(deviations);
            if (mad == 0d) return (0d, false);
            z = 0.6745 * (current - median) / mad;
        }
        else
        {
            var mean = 0d;
            foreach (var v in history) mean += v;
            mean /= history.Count;

            var sumSq = 0d;
            foreach (var v in history) sumSq += (v - mean) * (v - mean);
            // Sample standard deviation (n-1); history.Count >= MinHistory (>= 4) so the divisor is safe.
            var sd = Math.Sqrt(sumSq / (history.Count - 1));
            if (sd == 0d) return (0d, false);
            z = (current - mean) / sd;
        }

        return (z, Math.Abs(z) >= threshold);
    }

    private static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.ToArray();
        Array.Sort(sorted);
        var n = sorted.Length;
        if (n == 0) return 0d;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2d;
    }
}
