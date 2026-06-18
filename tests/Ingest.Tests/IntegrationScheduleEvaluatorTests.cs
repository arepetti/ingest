using Ingest.Core.Entities;
using Ingest.Core.Integrations;

namespace Ingest.Tests;

/// <summary>
/// Unit tests for <see cref="IntegrationScheduleEvaluator"/> — the pure "is this schedule due now?"
/// decision. Covers the time-of-day gate and every frequency, including the anchor-month cycle for
/// Quarterly/Semi-annually, the day-of-month clamp in short months, the last-day option, and the
/// forgiving "on or after day N" rule that the per-period outbox dedupe relies on.
/// </summary>
public class IntegrationScheduleEvaluatorTests
{
    private static IntegrationSchedule Schedule(
        IntegrationFrequency frequency,
        int hourUtc = 8,
        int minuteUtc = 0,
        IEnumerable<DayOfWeek>? days = null,
        int dayOfMonth = 1,
        bool lastDayOfMonth = false,
        int anchorMonth = 1) => new()
    {
        Frequency = frequency,
        HourUtc = hourUtc,
        MinuteUtc = minuteUtc,
        Days = days?.ToList() ?? new(),
        DayOfMonth = dayOfMonth,
        LastDayOfMonth = lastDayOfMonth,
        AnchorMonth = anchorMonth,
    };

    // A Wednesday at 09:00 UTC; the 15th of a 31-day month.
    private static readonly DateTime Wed0900 = new(2026, 7, 15, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Time_gate_blocks_before_the_trigger()
    {
        var s = Schedule(IntegrationFrequency.Daily, hourUtc: 8, minuteUtc: 30);
        Assert.False(IntegrationScheduleEvaluator.IsDue(s, new DateTime(2026, 7, 15, 8, 29, 0, DateTimeKind.Utc)));
        Assert.True(IntegrationScheduleEvaluator.IsDue(s, new DateTime(2026, 7, 15, 8, 30, 0, DateTimeKind.Utc)));
        Assert.True(IntegrationScheduleEvaluator.IsDue(s, new DateTime(2026, 7, 15, 23, 59, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Daily_is_due_any_day_once_past_the_trigger()
    {
        var s = Schedule(IntegrationFrequency.Daily);
        Assert.True(IntegrationScheduleEvaluator.IsDue(s, Wed0900));
        Assert.True(IntegrationScheduleEvaluator.IsDue(s, Wed0900.AddDays(1)));
    }

    [Fact]
    public void Weekly_matches_only_selected_weekdays()
    {
        var s = Schedule(IntegrationFrequency.Weekly, days: new[] { DayOfWeek.Wednesday });
        Assert.True(IntegrationScheduleEvaluator.IsDue(s, Wed0900));                 // Wed
        Assert.False(IntegrationScheduleEvaluator.IsDue(s, Wed0900.AddDays(1)));     // Thu
    }

    [Fact]
    public void Weekly_with_no_days_is_every_day()
    {
        var s = Schedule(IntegrationFrequency.Weekly);
        Assert.True(IntegrationScheduleEvaluator.IsDue(s, Wed0900));
        Assert.True(IntegrationScheduleEvaluator.IsDue(s, Wed0900.AddDays(3)));
    }

    [Fact]
    public void Monthly_is_due_on_or_after_the_target_day()
    {
        var s = Schedule(IntegrationFrequency.Monthly, dayOfMonth: 15);
        Assert.False(IntegrationScheduleEvaluator.IsDue(s, new DateTime(2026, 7, 14, 9, 0, 0, DateTimeKind.Utc)));
        Assert.True(IntegrationScheduleEvaluator.IsDue(s, new DateTime(2026, 7, 15, 9, 0, 0, DateTimeKind.Utc)));
        Assert.True(IntegrationScheduleEvaluator.IsDue(s, new DateTime(2026, 7, 20, 9, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Monthly_day_is_clamped_to_month_length()
    {
        // Day 31 in February (28 days, 2026) clamps to the 28th.
        var s = Schedule(IntegrationFrequency.Monthly, dayOfMonth: 31);
        Assert.False(IntegrationScheduleEvaluator.IsDue(s, new DateTime(2026, 2, 27, 9, 0, 0, DateTimeKind.Utc)));
        Assert.True(IntegrationScheduleEvaluator.IsDue(s, new DateTime(2026, 2, 28, 9, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Last_day_of_month_matches_only_the_final_day()
    {
        var s = Schedule(IntegrationFrequency.Monthly, lastDayOfMonth: true);
        Assert.False(IntegrationScheduleEvaluator.IsDue(s, new DateTime(2026, 7, 30, 9, 0, 0, DateTimeKind.Utc)));
        Assert.True(IntegrationScheduleEvaluator.IsDue(s, new DateTime(2026, 7, 31, 9, 0, 0, DateTimeKind.Utc)));
        // February: the 28th is the last day in 2026.
        Assert.True(IntegrationScheduleEvaluator.IsDue(s, new DateTime(2026, 2, 28, 9, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Quarterly_is_due_only_in_anchor_cycle_months()
    {
        // Anchor February -> eligible months Feb, May, Aug, Nov.
        var s = Schedule(IntegrationFrequency.Quarterly, dayOfMonth: 1, anchorMonth: 2);
        Assert.True(IntegrationScheduleEvaluator.IsDue(s, new DateTime(2026, 2, 1, 9, 0, 0, DateTimeKind.Utc)));
        Assert.True(IntegrationScheduleEvaluator.IsDue(s, new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc)));
        Assert.True(IntegrationScheduleEvaluator.IsDue(s, new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc)));
        Assert.False(IntegrationScheduleEvaluator.IsDue(s, new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc)));
        Assert.False(IntegrationScheduleEvaluator.IsDue(s, new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void SemiAnnually_is_due_every_six_months_from_the_anchor()
    {
        // Anchor March -> eligible months Mar and Sep.
        var s = Schedule(IntegrationFrequency.SemiAnnually, dayOfMonth: 1, anchorMonth: 3);
        Assert.True(IntegrationScheduleEvaluator.IsDue(s, new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc)));
        Assert.True(IntegrationScheduleEvaluator.IsDue(s, new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc)));
        Assert.False(IntegrationScheduleEvaluator.IsDue(s, new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Yearly_is_due_only_in_the_anchor_month()
    {
        var s = Schedule(IntegrationFrequency.Yearly, dayOfMonth: 10, anchorMonth: 4);
        Assert.False(IntegrationScheduleEvaluator.IsDue(s, new DateTime(2026, 4, 9, 9, 0, 0, DateTimeKind.Utc)));
        Assert.True(IntegrationScheduleEvaluator.IsDue(s, new DateTime(2026, 4, 10, 9, 0, 0, DateTimeKind.Utc)));
        Assert.True(IntegrationScheduleEvaluator.IsDue(s, new DateTime(2026, 4, 28, 9, 0, 0, DateTimeKind.Utc)));
        Assert.False(IntegrationScheduleEvaluator.IsDue(s, new DateTime(2026, 5, 10, 9, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Day_gate_still_respects_the_time_of_day()
    {
        var s = Schedule(IntegrationFrequency.Monthly, hourUtc: 8, dayOfMonth: 15);
        Assert.False(IntegrationScheduleEvaluator.IsDue(s, new DateTime(2026, 7, 15, 7, 59, 0, DateTimeKind.Utc)));
        Assert.True(IntegrationScheduleEvaluator.IsDue(s, new DateTime(2026, 7, 15, 8, 0, 0, DateTimeKind.Utc)));
    }
}
