using Ingest.Core.Entities;
using Ingest.Core.Validation;

namespace Ingest.Tests;

public class CadenceCalculatorTests
{
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
