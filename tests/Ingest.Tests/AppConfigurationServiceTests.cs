using Ingest.Core.Entities;
using Ingest.Core.Validation;
using Ingest.Infrastructure.Configuration;

namespace Ingest.Tests;

/// <summary>
/// Tests for the pure helpers <see cref="AppConfigurationService"/> applies before it persists the
/// configuration: area-list normalization and resolving the nullable anchor fields on the
/// <see cref="AppConfiguration"/> singleton to a concrete <see cref="CadenceAnchors"/>. Persistence
/// itself is Mongo-backed and covered by integration tests; here we pin the pure semantics.
/// </summary>
public class AppConfigurationServiceTests
{
    [Fact]
    public void Normalize_trims_drops_blanks_and_preserves_order()
    {
        var result = AppConfigurationService.Normalize(new[] { "  North  ", "", "   ", "South" });
        Assert.Equal(new[] { "North", "South" }, result);
    }

    [Fact]
    public void Normalize_deduplicates_case_insensitively_keeping_the_first_spelling()
    {
        var result = AppConfigurationService.Normalize(new[] { "North", "north", "NORTH", "East" });
        Assert.Equal(new[] { "North", "East" }, result);
    }

    [Fact]
    public void Normalize_handles_a_null_input()
    {
        Assert.Empty(AppConfigurationService.Normalize(null));
    }

    // ── ToAnchors ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ToAnchors_resolves_a_missing_document_to_the_historical_defaults()
    {
        Assert.Equal(CadenceAnchors.Default, AppConfigurationService.ToAnchors(null));
    }

    [Fact]
    public void ToAnchors_resolves_an_untouched_document_to_the_historical_defaults()
    {
        // A document that only carries Areas (the original use of the singleton) must resolve to
        // exactly the same defaults as a missing document — legacy deployments are unaffected.
        var config = new AppConfiguration { Areas = new List<string> { "North" } };
        Assert.Equal(CadenceAnchors.Default, AppConfigurationService.ToAnchors(config));
    }

    [Fact]
    public void ToAnchors_uses_every_configured_field_when_all_are_set()
    {
        var config = new AppConfiguration
        {
            FiscalYearStartMonth = 4,
            WeekStartDay = DayOfWeek.Sunday,
            MonthStartDay = 15,
            FortnightAnchor = new DateTime(2020, 6, 6, 0, 0, 0, DateTimeKind.Utc),
        };
        var anchors = AppConfigurationService.ToAnchors(config);
        Assert.Equal(4, anchors.FiscalYearStartMonth);
        Assert.Equal(DayOfWeek.Sunday, anchors.WeekStartDay);
        Assert.Equal(15, anchors.MonthStartDay);
        Assert.Equal(new DateTime(2020, 6, 6, 0, 0, 0, DateTimeKind.Utc), anchors.FortnightAnchor);
    }

    [Fact]
    public void ToAnchors_falls_back_field_by_field_when_only_some_are_set()
    {
        // Only the fiscal year start is configured; every other anchor still resolves to its own
        // individual default rather than the whole record collapsing to defaults.
        var config = new AppConfiguration { FiscalYearStartMonth = 10 };
        var anchors = AppConfigurationService.ToAnchors(config);
        Assert.Equal(10, anchors.FiscalYearStartMonth);
        Assert.Equal(CadenceAnchors.Default.WeekStartDay, anchors.WeekStartDay);
        Assert.Equal(CadenceAnchors.Default.MonthStartDay, anchors.MonthStartDay);
        Assert.Equal(CadenceAnchors.Default.FortnightAnchor, anchors.FortnightAnchor);
    }

    [Fact]
    public void ToAnchors_normalizes_a_stored_fortnight_anchor_to_utc_midnight()
    {
        // A value stored with a time-of-day component or a non-UTC Kind (e.g. round-tripped
        // through a driver that doesn't preserve Kind) must still resolve to a UTC midnight date.
        var config = new AppConfiguration { FortnightAnchor = new DateTime(2020, 6, 6, 13, 45, 0, DateTimeKind.Unspecified) };
        var anchors = AppConfigurationService.ToAnchors(config);
        Assert.Equal(new DateTime(2020, 6, 6, 0, 0, 0, DateTimeKind.Utc), anchors.FortnightAnchor);
        Assert.Equal(DateTimeKind.Utc, anchors.FortnightAnchor.Kind);
    }

    // ── ToWindows ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ToWindows_resolves_a_missing_document_to_the_defaults()
    {
        Assert.Equal(CadenceWindows.Default, AppConfigurationService.ToWindows(null));
    }

    [Fact]
    public void ToWindows_resolves_an_untouched_document_to_the_defaults()
    {
        var config = new AppConfiguration { Areas = new List<string> { "North" } };
        Assert.Equal(CadenceWindows.Default, AppConfigurationService.ToWindows(config));
    }

    [Fact]
    public void ToWindows_uses_every_configured_cadence_when_all_are_set()
    {
        var config = new AppConfiguration
        {
            CadenceWindows = new CadenceWindowSettings
            {
                Daily = new CadenceWindowOverride { OpenOffsetHours = 1, GraceHours = 2 },
                Weekly = new CadenceWindowOverride { OpenOffsetHours = 3, GraceHours = 4 },
                Fortnightly = new CadenceWindowOverride { OpenOffsetHours = 5, GraceHours = 6 },
                Monthly = new CadenceWindowOverride { OpenOffsetHours = 7, GraceHours = 8 },
                Quarterly = new CadenceWindowOverride { OpenOffsetHours = 9, GraceHours = 10 },
                SemiAnnually = new CadenceWindowOverride { OpenOffsetHours = 11, GraceHours = 12 },
                Yearly = new CadenceWindowOverride { OpenOffsetHours = 13, GraceHours = 14 },
            },
        };
        var windows = AppConfigurationService.ToWindows(config);
        Assert.Equal(new CadenceWindow(1, 2), windows.Daily);
        Assert.Equal(new CadenceWindow(3, 4), windows.Weekly);
        Assert.Equal(new CadenceWindow(5, 6), windows.Fortnightly);
        Assert.Equal(new CadenceWindow(7, 8), windows.Monthly);
        Assert.Equal(new CadenceWindow(9, 10), windows.Quarterly);
        Assert.Equal(new CadenceWindow(11, 12), windows.SemiAnnually);
        Assert.Equal(new CadenceWindow(13, 14), windows.Yearly);
    }

    [Fact]
    public void ToWindows_falls_back_cadence_by_cadence_when_only_some_are_set()
    {
        // Only Weekly is configured; every other cadence still resolves to CadenceWindow.None
        // rather than the whole record collapsing to defaults.
        var config = new AppConfiguration
        {
            CadenceWindows = new CadenceWindowSettings
            {
                Weekly = new CadenceWindowOverride { OpenOffsetHours = 24, GraceHours = 48 },
            },
        };
        var windows = AppConfigurationService.ToWindows(config);
        Assert.Equal(new CadenceWindow(24, 48), windows.Weekly);
        Assert.Equal(CadenceWindow.None, windows.Daily);
        Assert.Equal(CadenceWindow.None, windows.Monthly);
        Assert.Equal(CadenceWindow.None, windows.Yearly);
    }

    [Fact]
    public void ToWindows_a_partially_set_override_falls_back_field_by_field()
    {
        // Only GraceHours is set on the override; OpenOffsetHours still resolves to 0.
        var config = new AppConfiguration
        {
            CadenceWindows = new CadenceWindowSettings
            {
                Monthly = new CadenceWindowOverride { GraceHours = 72 },
            },
        };
        var windows = AppConfigurationService.ToWindows(config);
        Assert.Equal(0, windows.Monthly.OpenOffsetHours);
        Assert.Equal(72, windows.Monthly.GraceHours);
    }

    // ── Clamp ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Clamp_floors_negative_values_to_zero()
    {
        var clamped = AppConfigurationService.Clamp(new CadenceWindow(-5, -100));
        Assert.Equal(new CadenceWindow(0, 0), clamped);
    }

    [Fact]
    public void Clamp_caps_huge_values_to_the_maximum()
    {
        var clamped = AppConfigurationService.Clamp(new CadenceWindow(1_000_000, 1_000_000));
        Assert.Equal(new CadenceWindow(AppConfigurationService.MaxWindowHours, AppConfigurationService.MaxWindowHours), clamped);
    }

    [Fact]
    public void Clamp_leaves_in_range_values_untouched()
    {
        var clamped = AppConfigurationService.Clamp(new CadenceWindow(24, 48));
        Assert.Equal(new CadenceWindow(24, 48), clamped);
    }
}
