using Ingest.Core.Entities;

namespace Ingest.Core.Integrations;

/// <summary>
/// Pure evaluation of whether an <see cref="IntegrationSchedule"/> is eligible to run at a given
/// instant. Kept free of I/O so the (slightly fiddly) calendar logic is trivially unit-testable;
/// mirrors <see cref="IntegrationMatcher"/>.
/// </summary>
/// <remarks>
/// The schedule only decides <em>when to look</em>; the outbox dedupes per outstanding cadence
/// period, so the Monthly-and-longer frequencies use a forgiving "on or after day N" rule within
/// the eligible month — a one-day scheduler outage doesn't skip the period.
/// </remarks>
public static class IntegrationScheduleEvaluator
{
    /// <summary>True when <paramref name="schedule"/> is eligible to run at <paramref name="nowUtc"/>.</summary>
    public static bool IsDue(IntegrationSchedule schedule, DateTime nowUtc)
    {
        var trigger = new TimeSpan(Math.Clamp(schedule.HourUtc, 0, 23), Math.Clamp(schedule.MinuteUtc, 0, 59), 0);
        if (nowUtc.TimeOfDay < trigger) return false;

        return schedule.Frequency switch
        {
            IntegrationFrequency.Daily => true,
            IntegrationFrequency.Weekly => schedule.Days.Count == 0 || schedule.Days.Contains(nowUtc.DayOfWeek),
            IntegrationFrequency.Monthly => DayReached(schedule, nowUtc),
            IntegrationFrequency.Quarterly => MonthInCycle(schedule.AnchorMonth, nowUtc.Month, 3) && DayReached(schedule, nowUtc),
            IntegrationFrequency.SemiAnnually => MonthInCycle(schedule.AnchorMonth, nowUtc.Month, 6) && DayReached(schedule, nowUtc),
            IntegrationFrequency.Yearly => nowUtc.Month == Math.Clamp(schedule.AnchorMonth, 1, 12) && DayReached(schedule, nowUtc),
            _ => false,
        };
    }

    /// <summary>True when <paramref name="month"/> is on the every-<paramref name="every"/>-months cycle anchored at <paramref name="anchor"/>.</summary>
    private static bool MonthInCycle(int anchor, int month, int every)
    {
        anchor = Math.Clamp(anchor, 1, 12);
        return ((month - anchor) % every + every) % every == 0;
    }

    /// <summary>
    /// True when the day-of-month target has been reached for <paramref name="nowUtc"/>. The last-day
    /// option matches only the final day; otherwise the (month-length-clamped) target day is a
    /// "on or after" threshold so dedupe handles the single enqueue per period.
    /// </summary>
    private static bool DayReached(IntegrationSchedule schedule, DateTime nowUtc)
    {
        var daysInMonth = DateTime.DaysInMonth(nowUtc.Year, nowUtc.Month);
        if (schedule.LastDayOfMonth) return nowUtc.Day == daysInMonth;
        var target = Math.Min(Math.Clamp(schedule.DayOfMonth, 1, 31), daysInMonth);
        return nowUtc.Day >= target;
    }
}
