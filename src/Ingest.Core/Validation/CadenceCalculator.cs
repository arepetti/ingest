using Ingest.Core.Entities;

namespace Ingest.Core.Validation;

/// <summary>
/// Pure functions that map a timestamp onto the half-open <c>[start, end)</c> cadence bucket
/// containing it. Used by the validator (to enforce "one sample per bucket"), the status service
/// (to compute the current bucket for "satisfied this period?" checks) and the history service
/// (to group samples for charts).
/// </summary>
public static class CadenceCalculator
{
    // Reference Monday used as the epoch for fortnightly bucketing. 2001-01-01 is a Monday, so
    // every 14-day window aligned to this anchor lands on a Monday. Stored as a private constant
    // so consecutive fortnights never overlap regardless of when the schema was created — every
    // service sees the same biweek boundaries.
    private static readonly DateTime FortnightAnchor = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Return the bucket containing the given timestamp for a specific cadence.</summary>
    /// <param name="cadence">Cadence to align to.</param>
    /// <param name="timestampUtc">Timestamp to bucket; treated as UTC regardless of its <see cref="DateTimeKind"/>.</param>
    /// <returns>The inclusive start and exclusive end of the bucket.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cadence"/> is not a known enum value.</exception>
    public static (DateTime Start, DateTime End) BucketFor(Cadence cadence, DateTime timestampUtc)
    {
        var t = DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc);
        return cadence switch
        {
            Cadence.Daily => (new DateTime(t.Year, t.Month, t.Day, 0, 0, 0, DateTimeKind.Utc),
                              new DateTime(t.Year, t.Month, t.Day, 0, 0, 0, DateTimeKind.Utc).AddDays(1)),
            Cadence.Weekly => WeekBucket(t),
            Cadence.Fortnightly => FortnightBucket(t),
            Cadence.Monthly => (new DateTime(t.Year, t.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                                new DateTime(t.Year, t.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1)),
            Cadence.Quarterly => QuarterBucket(t),
            Cadence.SemiAnnually => HalfYearBucket(t),
            Cadence.Yearly => (new DateTime(t.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                               new DateTime(t.Year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            _ => throw new ArgumentOutOfRangeException(nameof(cadence)),
        };
    }

    /// <summary>
    /// Return the cadence bucket immediately preceding the one containing
    /// <paramref name="timestampUtc"/>. Convenience wrapper over <see cref="BucketAtOffset"/>
    /// with an offset of <c>-1</c>; the returned bucket's <c>End</c> always equals the current
    /// bucket's <c>Start</c> (buckets are contiguous).
    /// </summary>
    /// <param name="cadence">Cadence to align to.</param>
    /// <param name="timestampUtc">Reference timestamp; treated as UTC.</param>
    /// <returns>The inclusive start and exclusive end of the previous bucket.</returns>
    public static (DateTime Start, DateTime End) PreviousBucketFor(Cadence cadence, DateTime timestampUtc)
        => BucketAtOffset(cadence, timestampUtc, -1);

    /// <summary>
    /// Return the cadence bucket at a signed <paramref name="offset"/> from the bucket containing
    /// <paramref name="timestampUtc"/>. Offset <c>0</c> is the current bucket, <c>-1</c> the
    /// previous one, <c>+1</c> the next, and so on. Because buckets are contiguous half-open
    /// intervals, stepping is done by nudging one tick past the relevant boundary.
    /// </summary>
    /// <param name="cadence">Cadence to align to.</param>
    /// <param name="timestampUtc">Reference timestamp; treated as UTC.</param>
    /// <param name="offset">Signed number of buckets to move (negative = past, positive = future).</param>
    /// <returns>The inclusive start and exclusive end of the offset bucket.</returns>
    public static (DateTime Start, DateTime End) BucketAtOffset(Cadence cadence, DateTime timestampUtc, int offset)
    {
        var (start, end) = BucketFor(cadence, timestampUtc);
        if (offset < 0)
            for (int i = 0; i < -offset; i++)
                (start, end) = BucketFor(cadence, start.AddTicks(-1));
        else if (offset > 0)
            for (int i = 0; i < offset; i++)
                (start, end) = BucketFor(cadence, end); // `end` is exclusive → first instant of the next bucket
        return (start, end);
    }

    private static (DateTime Start, DateTime End) WeekBucket(DateTime t)
    {
        int diff = ((int)t.DayOfWeek + 6) % 7;
        var start = new DateTime(t.Year, t.Month, t.Day, 0, 0, 0, DateTimeKind.Utc).AddDays(-diff);
        return (start, start.AddDays(7));
    }

    private static (DateTime Start, DateTime End) FortnightBucket(DateTime t)
    {
        // Bucket by integer-dividing the day count since the anchor. Math.Floor handles negative
        // diffs (timestamps before the anchor) so the buckets stay consistent in both directions.
        var dateOnly = new DateTime(t.Year, t.Month, t.Day, 0, 0, 0, DateTimeKind.Utc);
        var days = (long)(dateOnly - FortnightAnchor).TotalDays;
        // Math.Floor(...) over a double to avoid C#'s truncate-toward-zero on integer division
        // (which would map -3/14 to 0 instead of -1 and shift pre-anchor buckets by one).
        var bucket = (long)Math.Floor((double)days / 14);
        var start = FortnightAnchor.AddDays(bucket * 14);
        return (start, start.AddDays(14));
    }

    private static (DateTime Start, DateTime End) QuarterBucket(DateTime t)
    {
        int qIdx = (t.Month - 1) / 3;
        int startMonth = qIdx * 3 + 1;
        var start = new DateTime(t.Year, startMonth, 1, 0, 0, 0, DateTimeKind.Utc);
        return (start, start.AddMonths(3));
    }

    private static (DateTime Start, DateTime End) HalfYearBucket(DateTime t)
    {
        int startMonth = t.Month <= 6 ? 1 : 7;
        var start = new DateTime(t.Year, startMonth, 1, 0, 0, 0, DateTimeKind.Utc);
        return (start, start.AddMonths(6));
    }

    /// <summary>
    /// Resolve a free-form period name (<c>day</c>, <c>week</c>, <c>fortnight</c>, <c>month</c>,
    /// <c>quarter</c>, <c>halfyear</c>/<c>semiannual</c>, <c>year</c>) into the equivalent
    /// cadence bucket containing <paramref name="nowUtc"/>. Unknown strings fall back to weekly
    /// to match the default <c>Ingest:DefaultStatusPeriod</c>.
    /// </summary>
    /// <param name="period">Period name (case-insensitive). Accepts the short alias above as well as the longer enum-style name.</param>
    /// <param name="nowUtc">Reference timestamp; usually "now".</param>
    /// <returns>The inclusive start and exclusive end of the matching bucket.</returns>
    public static (DateTime Start, DateTime End) BucketForPeriod(string period, DateTime nowUtc) =>
        period.ToLowerInvariant() switch
        {
            "day" or "daily" => BucketFor(Cadence.Daily, nowUtc),
            "week" or "weekly" => BucketFor(Cadence.Weekly, nowUtc),
            "fortnight" or "fortnightly" or "biweek" or "biweekly" => BucketFor(Cadence.Fortnightly, nowUtc),
            "month" or "monthly" => BucketFor(Cadence.Monthly, nowUtc),
            "quarter" or "quarterly" => BucketFor(Cadence.Quarterly, nowUtc),
            "halfyear" or "half-year" or "semiannual" or "semiannually" or "semi-annual" => BucketFor(Cadence.SemiAnnually, nowUtc),
            "year" or "yearly" => BucketFor(Cadence.Yearly, nowUtc),
            _ => BucketFor(Cadence.Weekly, nowUtc),
        };
}
