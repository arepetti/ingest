using Ingest.Core.Entities;
using Ingest.Core.Validation;

namespace Ingest.Tests;

public class CadenceCalculatorTests
{
    // ── Configurable anchors ────────────────────────────────────────────────────────────────
    // These pin the non-default behaviour introduced by CadenceAnchors; every test above this
    // region already proves the *default* (null anchors / CadenceAnchors.Default) behaviour is
    // unchanged.

    [Fact]
    public void Weekly_honours_a_non_monday_week_start()
    {
        // 2026-05-15 is a Friday. With Sunday as the week start, the bucket begins on the
        // preceding Sunday (2026-05-10).
        var anchors = CadenceAnchors.Default with { WeekStartDay = DayOfWeek.Sunday };
        var t = new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc);
        var (s, e) = CadenceCalculator.BucketFor(Cadence.Weekly, t, anchors);
        Assert.Equal(new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc), s);
        Assert.Equal(new DateTime(2026, 5, 17, 0, 0, 0, DateTimeKind.Utc), e);
    }

    [Fact]
    public void Monthly_honours_a_non_first_start_day()
    {
        // With month-start day 15, a timestamp on the 10th falls in the bucket that started on
        // the 15th of the *previous* month.
        var anchors = CadenceAnchors.Default with { MonthStartDay = 15 };
        var t = new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc);
        var (s, e) = CadenceCalculator.BucketFor(Cadence.Monthly, t, anchors);
        Assert.Equal(new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc), s);
        Assert.Equal(new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc), e);
    }

    [Fact]
    public void Monthly_start_day_is_clamped_to_28()
    {
        // 30/31 would misbehave in February; the configured value is clamped to 28 so every
        // month has a bucket boundary.
        var anchors = CadenceAnchors.Default with { MonthStartDay = 31 };
        var t = new DateTime(2026, 5, 29, 0, 0, 0, DateTimeKind.Utc);
        var (s, e) = CadenceCalculator.BucketFor(Cadence.Monthly, t, anchors);
        Assert.Equal(new DateTime(2026, 5, 28, 0, 0, 0, DateTimeKind.Utc), s);
        Assert.Equal(new DateTime(2026, 6, 28, 0, 0, 0, DateTimeKind.Utc), e);
    }

    [Fact]
    public void Fortnightly_honours_a_custom_anchor()
    {
        // Anchor shifted a week later than the default (2001-01-08 instead of 2001-01-01).
        var anchors = CadenceAnchors.Default with { FortnightAnchor = new DateTime(2001, 1, 8, 0, 0, 0, DateTimeKind.Utc) };
        var t = new DateTime(2026, 5, 15, 13, 30, 0, DateTimeKind.Utc); // was 2026-05-04..18 by default
        var (s, e) = CadenceCalculator.BucketFor(Cadence.Fortnightly, t, anchors);
        Assert.Equal(new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc), s);
        Assert.Equal(new DateTime(2026, 5, 25, 0, 0, 0, DateTimeKind.Utc), e);
    }

    [Fact]
    public void Quarterly_honours_a_non_january_fiscal_year_start_before_the_new_fiscal_year()
    {
        // Fiscal year starts in July. A May 2026 timestamp is still inside the fiscal year that
        // started July 2025 → its Q4, i.e. Apr-Jun 2026.
        var anchors = CadenceAnchors.Default with { FiscalYearStartMonth = 7 };
        var t = new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc);
        var (s, e) = CadenceCalculator.BucketFor(Cadence.Quarterly, t, anchors);
        Assert.Equal(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), s);
        Assert.Equal(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), e);
    }

    [Fact]
    public void Quarterly_honours_a_non_january_fiscal_year_start_after_the_new_fiscal_year()
    {
        // Same July fiscal start; an August 2026 timestamp is in the new fiscal year's Q1 (Jul-Sep).
        var anchors = CadenceAnchors.Default with { FiscalYearStartMonth = 7 };
        var t = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);
        var (s, e) = CadenceCalculator.BucketFor(Cadence.Quarterly, t, anchors);
        Assert.Equal(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), s);
        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), e);
    }

    [Fact]
    public void Yearly_honours_a_non_january_fiscal_year_start()
    {
        // Fiscal year starts in July: a May 2026 timestamp is still in the fiscal year that
        // started July 2025.
        var anchors = CadenceAnchors.Default with { FiscalYearStartMonth = 7 };
        var t = new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc);
        var (s, e) = CadenceCalculator.BucketFor(Cadence.Yearly, t, anchors);
        Assert.Equal(new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc), s);
        Assert.Equal(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), e);
    }

    [Fact]
    public void SemiAnnually_honours_a_non_january_fiscal_year_start()
    {
        var anchors = CadenceAnchors.Default with { FiscalYearStartMonth = 4 }; // April fiscal start
        var t = new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc); // in the second half (Oct-Mar)
        var (s, e) = CadenceCalculator.BucketFor(Cadence.SemiAnnually, t, anchors);
        Assert.Equal(new DateTime(2025, 10, 1, 0, 0, 0, DateTimeKind.Utc), s);
        Assert.Equal(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), e);
    }

    [Fact]
    public void Daily_ignores_anchors()
    {
        // Daily has no anchor point at all; a non-default CadenceAnchors must not change it.
        var anchors = new CadenceAnchors(FiscalYearStartMonth: 4, WeekStartDay: DayOfWeek.Wednesday,
            MonthStartDay: 10, FortnightAnchor: new DateTime(2020, 6, 6, 0, 0, 0, DateTimeKind.Utc));
        var t = new DateTime(2026, 5, 15, 13, 45, 0, DateTimeKind.Utc);
        Assert.Equal(CadenceCalculator.BucketFor(Cadence.Daily, t), CadenceCalculator.BucketFor(Cadence.Daily, t, anchors));
    }

    [Fact]
    public void Null_anchors_is_equivalent_to_the_explicit_default()
    {
        var t = new DateTime(2026, 5, 15, 13, 45, 0, DateTimeKind.Utc);
        foreach (var cadence in Enum.GetValues<Cadence>())
            Assert.Equal(CadenceCalculator.BucketFor(cadence, t), CadenceCalculator.BucketFor(cadence, t, CadenceAnchors.Default));
    }

    // ── WindowFor (submission window: bucket extended by open offset / grace) ─────────────────

    [Fact]
    public void WindowFor_with_null_windows_is_exactly_the_bucket()
    {
        var t = new DateTime(2026, 5, 15, 13, 45, 0, DateTimeKind.Utc);
        foreach (var cadence in Enum.GetValues<Cadence>())
        {
            var bucket = CadenceCalculator.BucketFor(cadence, t);
            Assert.Equal(bucket, CadenceCalculator.WindowFor(cadence, t));
        }
    }

    [Fact]
    public void WindowFor_with_default_windows_is_exactly_the_bucket()
    {
        var t = new DateTime(2026, 5, 15, 13, 45, 0, DateTimeKind.Utc);
        foreach (var cadence in Enum.GetValues<Cadence>())
        {
            var bucket = CadenceCalculator.BucketFor(cadence, t);
            Assert.Equal(bucket, CadenceCalculator.WindowFor(cadence, t, anchors: null, windows: CadenceWindows.Default));
        }
    }

    [Fact]
    public void WindowFor_open_offset_delays_the_start_only()
    {
        var t = new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc); // Friday, inside Weekly bucket [5-11, 5-18)
        var windows = CadenceWindows.Default with { Weekly = new CadenceWindow(OpenOffsetHours: 24, GraceHours: 0) };
        var (start, end) = CadenceCalculator.WindowFor(Cadence.Weekly, t, anchors: null, windows: windows);
        Assert.Equal(new DateTime(2026, 5, 12, 0, 0, 0, DateTimeKind.Utc), start); // bucket start + 24h
        Assert.Equal(new DateTime(2026, 5, 18, 0, 0, 0, DateTimeKind.Utc), end);   // bucket end, unchanged
    }

    [Fact]
    public void WindowFor_grace_extends_the_end_only()
    {
        var t = new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc);
        var windows = CadenceWindows.Default with { Weekly = new CadenceWindow(OpenOffsetHours: 0, GraceHours: 48) };
        var (start, end) = CadenceCalculator.WindowFor(Cadence.Weekly, t, anchors: null, windows: windows);
        Assert.Equal(new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc), start); // bucket start, unchanged
        Assert.Equal(new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc), end);   // bucket end + 48h
    }

    [Fact]
    public void WindowFor_only_applies_the_configured_cadences_window_not_others()
    {
        var t = new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc);
        var windows = CadenceWindows.Default with { Weekly = new CadenceWindow(24, 48) };
        // Monthly's window for the same instant must be unaffected by the Weekly override.
        Assert.Equal(
            CadenceCalculator.BucketFor(Cadence.Monthly, t),
            CadenceCalculator.WindowFor(Cadence.Monthly, t, anchors: null, windows: windows));
    }

    [Fact]
    public void WindowFor_composes_with_custom_anchors()
    {
        // Non-default week start (Sunday) combined with a non-zero open offset/grace: the bucket
        // math and the window offset each apply independently.
        var anchors = CadenceAnchors.Default with { WeekStartDay = DayOfWeek.Sunday };
        var windows = CadenceWindows.Default with { Weekly = new CadenceWindow(OpenOffsetHours: 12, GraceHours: 6) };
        var t = new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc); // Friday
        var (bucketStart, bucketEnd) = CadenceCalculator.BucketFor(Cadence.Weekly, t, anchors);
        var (windowStart, windowEnd) = CadenceCalculator.WindowFor(Cadence.Weekly, t, anchors, windows);
        Assert.Equal(bucketStart.AddHours(12), windowStart);
        Assert.Equal(bucketEnd.AddHours(6), windowEnd);
    }

    [Fact]
    public void Daily_buckets_one_day()
    {
        var t = new DateTime(2026, 5, 15, 13, 45, 0, DateTimeKind.Utc);
        var (s, e) = CadenceCalculator.BucketFor(Cadence.Daily, t);
        Assert.Equal(new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc), s);
        Assert.Equal(new DateTime(2026, 5, 16, 0, 0, 0, DateTimeKind.Utc), e);
    }

    [Fact]
    public void Weekly_starts_on_monday()
    {
        // 2026-05-15 is a Friday
        var t = new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc);
        var (s, e) = CadenceCalculator.BucketFor(Cadence.Weekly, t);
        Assert.Equal(new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc), s); // Monday
        Assert.Equal(new DateTime(2026, 5, 18, 0, 0, 0, DateTimeKind.Utc), e);
    }

    [Fact]
    public void Monthly_buckets_one_calendar_month()
    {
        var t = new DateTime(2026, 5, 28, 9, 0, 0, DateTimeKind.Utc);
        var (s, e) = CadenceCalculator.BucketFor(Cadence.Monthly, t);
        Assert.Equal(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), s);
        Assert.Equal(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), e);
    }

    [Fact]
    public void Yearly_buckets_one_calendar_year()
    {
        var t = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var (s, e) = CadenceCalculator.BucketFor(Cadence.Yearly, t);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), s);
        Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), e);
    }

    // ── Fortnightly ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Fortnightly_anchor_is_a_monday()
    {
        // 2001-01-01 is the documented anchor; verify it's actually a Monday so the buckets
        // really do line up on Mondays.
        var anchor = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(DayOfWeek.Monday, anchor.DayOfWeek);
        var (s, e) = CadenceCalculator.BucketFor(Cadence.Fortnightly, anchor);
        Assert.Equal(anchor, s);
        Assert.Equal(anchor.AddDays(14), e);
    }

    [Fact]
    public void Fortnightly_bucket_is_14_days_and_monday_aligned()
    {
        // Pick a weekday inside the bucket and verify the start/end pair. With the 2001-01-01
        // anchor, the fortnight containing Fri 2026-05-15 starts on Mon 2026-05-04 (May 15 is
        // in the second week of that biweek).
        var t = new DateTime(2026, 5, 15, 13, 30, 0, DateTimeKind.Utc);
        var (s, e) = CadenceCalculator.BucketFor(Cadence.Fortnightly, t);
        Assert.Equal(new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc), s);
        Assert.Equal(new DateTime(2026, 5, 18, 0, 0, 0, DateTimeKind.Utc), e);
        Assert.Equal(DayOfWeek.Monday, s.DayOfWeek);
    }

    [Fact]
    public void Fortnightly_consecutive_windows_are_disjoint_and_contiguous()
    {
        // End of one bucket must equal start of the next — no overlap, no gap.
        var t1 = new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc); // start of a fortnight
        var t2 = new DateTime(2026, 5, 18, 0, 0, 0, DateTimeKind.Utc); // first instant of next
        var (s1, e1) = CadenceCalculator.BucketFor(Cadence.Fortnightly, t1);
        var (s2, _)  = CadenceCalculator.BucketFor(Cadence.Fortnightly, t2);
        Assert.Equal(e1, s2);
        Assert.NotEqual(s1, s2);
    }

    [Fact]
    public void Fortnightly_handles_dates_before_the_anchor()
    {
        // 2000-12-31 is the Sunday just before the anchor. It should fall in the bucket that
        // ENDS at the anchor — i.e. [2000-12-18, 2001-01-01).
        var t = new DateTime(2000, 12, 31, 23, 59, 59, DateTimeKind.Utc);
        var (s, e) = CadenceCalculator.BucketFor(Cadence.Fortnightly, t);
        Assert.Equal(new DateTime(2000, 12, 18, 0, 0, 0, DateTimeKind.Utc), s);
        Assert.Equal(new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc), e);
    }

    // ── Quarterly ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 1, 4)]   // January → Q1: Jan–Mar (start month 1, end month 4)
    [InlineData(3, 1, 4)]   // March → still Q1
    [InlineData(4, 4, 7)]   // April → Q2: Apr–Jun
    [InlineData(7, 7, 10)]  // July → Q3: Jul–Sep
    [InlineData(10, 10, 1)] // October → Q4: Oct–Dec, end rolls to next year January
    [InlineData(12, 10, 1)] // December → still Q4
    public void Quarterly_aligns_to_calendar_quarters(int month, int expectedStartMonth, int expectedEndMonth)
    {
        var t = new DateTime(2026, month, 15, 12, 0, 0, DateTimeKind.Utc);
        var (s, e) = CadenceCalculator.BucketFor(Cadence.Quarterly, t);
        Assert.Equal(new DateTime(2026, expectedStartMonth, 1, 0, 0, 0, DateTimeKind.Utc), s);
        var expectedEnd = expectedEndMonth == 1
            ? new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            : new DateTime(2026, expectedEndMonth, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expectedEnd, e);
    }

    // ── SemiAnnually ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SemiAnnually_first_half_runs_jan_to_jun()
    {
        var t = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);
        var (s, e) = CadenceCalculator.BucketFor(Cadence.SemiAnnually, t);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), s);
        Assert.Equal(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), e);
    }

    [Fact]
    public void SemiAnnually_second_half_runs_jul_to_dec()
    {
        var t = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
        var (s, e) = CadenceCalculator.BucketFor(Cadence.SemiAnnually, t);
        Assert.Equal(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), s);
        Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), e);
    }

    // ── BucketForPeriod aliases ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("fortnight", Cadence.Fortnightly)]
    [InlineData("biweekly", Cadence.Fortnightly)]
    [InlineData("quarter", Cadence.Quarterly)]
    [InlineData("quarterly", Cadence.Quarterly)]
    [InlineData("halfyear", Cadence.SemiAnnually)]
    [InlineData("half-year", Cadence.SemiAnnually)]
    [InlineData("semiannual", Cadence.SemiAnnually)]
    [InlineData("semi-annual", Cadence.SemiAnnually)]
    public void BucketForPeriod_resolves_new_aliases(string alias, Cadence expected)
    {
        var now = new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(CadenceCalculator.BucketFor(expected, now), CadenceCalculator.BucketForPeriod(alias, now));
    }
}
