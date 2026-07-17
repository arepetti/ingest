namespace Ingest.Core.Validation;

/// <summary>
/// Configurable alignment points for cadence bucket math (see <see cref="CadenceCalculator"/>).
/// Resolved from the nullable fields on <c>AppConfiguration</c>; <see cref="Default"/> reproduces
/// the historical hard-coded calendar alignment (fiscal year = calendar year, week starts Monday,
/// month starts on the 1st, fortnights anchored to 2001-01-01) so a fresh or legacy deployment
/// with no configuration behaves exactly as it always did.
/// </summary>
/// <param name="FiscalYearStartMonth">
/// Month (1-12) the fiscal year begins on. Also anchors Quarterly and SemiAnnually as fiscal
/// sub-periods (quarter/half boundaries are computed relative to this month, not the calendar).
/// </param>
/// <param name="WeekStartDay">Day of week a Weekly bucket begins on.</param>
/// <param name="MonthStartDay">Day of month (1-28) a Monthly bucket begins on.</param>
/// <param name="FortnightAnchor">
/// A UTC midnight instant a Fortnightly bucket boundary is aligned to (only its date matters).
/// </param>
public sealed record CadenceAnchors(
    int FiscalYearStartMonth,
    DayOfWeek WeekStartDay,
    int MonthStartDay,
    DateTime FortnightAnchor)
{
    /// <summary>The historical hard-coded alignment, used when no configuration is present.</summary>
    public static readonly CadenceAnchors Default = new(
        FiscalYearStartMonth: 1,
        WeekStartDay: DayOfWeek.Monday,
        MonthStartDay: 1,
        FortnightAnchor: new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
}
