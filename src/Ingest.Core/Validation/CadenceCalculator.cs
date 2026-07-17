using Ingest.Core.Entities;

namespace Ingest.Core.Validation;

/// <summary>
/// Pure functions that map a timestamp onto the half-open <c>[start, end)</c> cadence bucket
/// containing it. Used by the validator (to enforce "one sample per bucket"), the status service
/// (to compute the current bucket for "satisfied this period?" checks) and the history service
/// (to group samples for charts).
/// </summary>
/// <remarks>
/// Every entry point takes an optional <see cref="CadenceAnchors"/>; <c>null</c> (the default)
/// resolves to <see cref="CadenceAnchors.Default"/>, which reproduces the original hard-coded
/// calendar alignment. This keeps every pre-existing call site (and test) compiling and behaving
/// unchanged while letting configured deployments anchor buckets to a fiscal year, a non-Monday
/// week start, a non-1st month start, or a custom fortnight boundary.
/// </remarks>
public static class CadenceCalculator
{
    /// <summary>Return the bucket containing the given timestamp for a specific cadence.</summary>
    /// <param name="cadence">Cadence to align to.</param>
    /// <param name="timestampUtc">Timestamp to bucket; treated as UTC regardless of its <see cref="DateTimeKind"/>.</param>
    /// <param name="anchors">Alignment points to use; <c>null</c> = the historical calendar defaults.</param>
    /// <returns>The inclusive start and exclusive end of the bucket.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cadence"/> is not a known enum value.</exception>
    public static (DateTime Start, DateTime End) BucketFor(Cadence cadence, DateTime timestampUtc, CadenceAnchors? anchors = null)
    {
        var a = anchors ?? CadenceAnchors.Default;
        var t = DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc);
        return cadence switch
        {
            Cadence.Daily => (new DateTime(t.Year, t.Month, t.Day, 0, 0, 0, DateTimeKind.Utc),
                              new DateTime(t.Year, t.Month, t.Day, 0, 0, 0, DateTimeKind.Utc).AddDays(1)),
            Cadence.Weekly => WeekBucket(t, a.WeekStartDay),
            Cadence.Fortnightly => FortnightBucket(t, a.FortnightAnchor),
            Cadence.Monthly => MonthBucket(t, a.MonthStartDay),
            Cadence.Quarterly => FiscalPeriodBucket(t, a.FiscalYearStartMonth, 3),
            Cadence.SemiAnnually => FiscalPeriodBucket(t, a.FiscalYearStartMonth, 6),
            Cadence.Yearly => FiscalPeriodBucket(t, a.FiscalYearStartMonth, 12),
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
    /// <param name="anchors">Alignment points to use; <c>null</c> = the historical calendar defaults.</param>
    /// <returns>The inclusive start and exclusive end of the previous bucket.</returns>
    public static (DateTime Start, DateTime End) PreviousBucketFor(Cadence cadence, DateTime timestampUtc, CadenceAnchors? anchors = null)
        => BucketAtOffset(cadence, timestampUtc, -1, anchors);

    /// <summary>
    /// Return the cadence bucket at a signed <paramref name="offset"/> from the bucket containing
    /// <paramref name="timestampUtc"/>. Offset <c>0</c> is the current bucket, <c>-1</c> the
    /// previous one, <c>+1</c> the next, and so on. Because buckets are contiguous half-open
    /// intervals, stepping is done by nudging one tick past the relevant boundary.
    /// </summary>
    /// <param name="cadence">Cadence to align to.</param>
    /// <param name="timestampUtc">Reference timestamp; treated as UTC.</param>
    /// <param name="offset">Signed number of buckets to move (negative = past, positive = future).</param>
    /// <param name="anchors">Alignment points to use; <c>null</c> = the historical calendar defaults.</param>
    /// <returns>The inclusive start and exclusive end of the offset bucket.</returns>
    public static (DateTime Start, DateTime End) BucketAtOffset(Cadence cadence, DateTime timestampUtc, int offset, CadenceAnchors? anchors = null)
    {
        var (start, end) = BucketFor(cadence, timestampUtc, anchors);
        if (offset < 0)
            for (int i = 0; i < -offset; i++)
                (start, end) = BucketFor(cadence, start.AddTicks(-1), anchors);
        else if (offset > 0)
            for (int i = 0; i < offset; i++)
                (start, end) = BucketFor(cadence, end, anchors); // `end` is exclusive → first instant of the next bucket
        return (start, end);
    }

    /// <summary>
    /// Return the submission <em>window</em> for the bucket containing <paramref name="timestampUtc"/> —
    /// <c>[bucket.Start + OpenOffsetHours, bucket.End + GraceHours)</c>, per the resolved
    /// <see cref="CadenceWindow"/> for this cadence. This is distinct from the bucket itself
    /// (<see cref="BucketFor"/>): the bucket is the period's identity (used for dedup/history), while
    /// the window is when a service is actually allowed to create/edit a sample for that period.
    /// </summary>
    /// <param name="cadence">Cadence to align to.</param>
    /// <param name="timestampUtc">Timestamp whose bucket's window is wanted; treated as UTC.</param>
    /// <param name="anchors">Bucket alignment points; <c>null</c> = the historical calendar defaults.</param>
    /// <param name="windows">Per-cadence open-offset/grace settings; <c>null</c> = no offset/grace (window == bucket).</param>
    /// <returns>The inclusive start and exclusive end of the submission window.</returns>
    public static (DateTime Start, DateTime End) WindowFor(
        Cadence cadence, DateTime timestampUtc, CadenceAnchors? anchors = null, CadenceWindows? windows = null)
    {
        var (start, end) = BucketFor(cadence, timestampUtc, anchors);
        var w = (windows ?? CadenceWindows.Default).For(cadence);
        return (start.AddHours(w.OpenOffsetHours), end.AddHours(w.GraceHours));
    }

    private static (DateTime Start, DateTime End) WeekBucket(DateTime t, DayOfWeek weekStartDay)
    {
        int diff = ((int)t.DayOfWeek - (int)weekStartDay + 7) % 7;
        var start = new DateTime(t.Year, t.Month, t.Day, 0, 0, 0, DateTimeKind.Utc).AddDays(-diff);
        return (start, start.AddDays(7));
    }

    private static (DateTime Start, DateTime End) MonthBucket(DateTime t, int monthStartDay)
    {
        var day = Math.Clamp(monthStartDay, 1, 28);
        var start = new DateTime(t.Year, t.Month, day, 0, 0, 0, DateTimeKind.Utc);
        if (t.Day < day) start = start.AddMonths(-1);
        return (start, start.AddMonths(1));
    }

    private static (DateTime Start, DateTime End) FortnightBucket(DateTime t, DateTime anchor)
    {
        // Bucket by integer-dividing the day count since the anchor. Math.Floor handles negative
        // diffs (timestamps before the anchor) so the buckets stay consistent in both directions.
        var dateOnly = new DateTime(t.Year, t.Month, t.Day, 0, 0, 0, DateTimeKind.Utc);
        var anchorDate = new DateTime(anchor.Year, anchor.Month, anchor.Day, 0, 0, 0, DateTimeKind.Utc);
        var days = (long)(dateOnly - anchorDate).TotalDays;
        // Math.Floor(...) over a double to avoid C#'s truncate-toward-zero on integer division
        // (which would map -3/14 to 0 instead of -1 and shift pre-anchor buckets by one).
        var bucket = (long)Math.Floor((double)days / 14);
        var start = anchorDate.AddDays(bucket * 14);
        return (start, start.AddDays(14));
    }

    /// <summary>
    /// Bucket <paramref name="t"/> into a <paramref name="periodMonths"/>-month block of the fiscal
    /// year starting on <paramref name="fiscalYearStartMonth"/>. Used for Yearly (12), SemiAnnually
    /// (6) and Quarterly (3) — each is just a different-sized sub-period of the same fiscal year.
    /// With the default start month of January this reduces exactly to the historical calendar
    /// year/half/quarter maths.
    /// </summary>
    private static (DateTime Start, DateTime End) FiscalPeriodBucket(DateTime t, int fiscalYearStartMonth, int periodMonths)
    {
        var startMonth = Math.Clamp(fiscalYearStartMonth, 1, 12);
        var fiscalYear = t.Month >= startMonth ? t.Year : t.Year - 1;
        var fyStart = new DateTime(fiscalYear, startMonth, 1, 0, 0, 0, DateTimeKind.Utc);

        var monthsFromFyStart = ((t.Year - fyStart.Year) * 12) + (t.Month - fyStart.Month);
        var blockIdx = monthsFromFyStart / periodMonths;
        var start = fyStart.AddMonths(blockIdx * periodMonths);
        return (start, start.AddMonths(periodMonths));
    }

    /// <summary>
    /// Resolve a free-form period name (<c>day</c>, <c>week</c>, <c>fortnight</c>, <c>month</c>,
    /// <c>quarter</c>, <c>halfyear</c>/<c>semiannual</c>, <c>year</c>) into the equivalent
    /// cadence bucket containing <paramref name="nowUtc"/>. Unknown strings fall back to weekly
    /// to match the default <c>Ingest:DefaultStatusPeriod</c>.
    /// </summary>
    /// <param name="period">Period name (case-insensitive). Accepts the short alias above as well as the longer enum-style name.</param>
    /// <param name="nowUtc">Reference timestamp; usually "now".</param>
    /// <param name="anchors">Alignment points to use; <c>null</c> = the historical calendar defaults.</param>
    /// <returns>The inclusive start and exclusive end of the matching bucket.</returns>
    public static (DateTime Start, DateTime End) BucketForPeriod(string period, DateTime nowUtc, CadenceAnchors? anchors = null) =>
        period.ToLowerInvariant() switch
        {
            "day" or "daily" => BucketFor(Cadence.Daily, nowUtc, anchors),
            "week" or "weekly" => BucketFor(Cadence.Weekly, nowUtc, anchors),
            "fortnight" or "fortnightly" or "biweek" or "biweekly" => BucketFor(Cadence.Fortnightly, nowUtc, anchors),
            "month" or "monthly" => BucketFor(Cadence.Monthly, nowUtc, anchors),
            "quarter" or "quarterly" => BucketFor(Cadence.Quarterly, nowUtc, anchors),
            "halfyear" or "half-year" or "semiannual" or "semiannually" or "semi-annual" => BucketFor(Cadence.SemiAnnually, nowUtc, anchors),
            "year" or "yearly" => BucketFor(Cadence.Yearly, nowUtc, anchors),
            _ => BucketFor(Cadence.Weekly, nowUtc, anchors),
        };
}
