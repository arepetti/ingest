using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Core.Validation;
using Ingest.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Configuration;

/// <summary>
/// Database-backed application configuration singleton. There is at most one document; an absent one
/// is treated as an empty configuration so fresh and legacy deployments are back-compatible.
/// </summary>
public sealed class AppConfigurationService : IAppConfigurationService
{
    private readonly MongoContext _ctx;
    private readonly IAuditContext _audit;

    /// <summary>Create a new <see cref="AppConfigurationService"/>.</summary>
    public AppConfigurationService(MongoContext ctx, IAuditContext audit)
    {
        _ctx = ctx;
        _audit = audit;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetAreasAsync(CancellationToken ct = default)
    {
        var existing = await LoadAsync(ct);
        return existing?.Areas ?? new List<string>();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> UpdateAreasAsync(IReadOnlyList<string> areas, CancellationToken ct = default)
    {
        var normalized = Normalize(areas);
        var config = await LoadOrCreateAsync(ct);
        config.Areas = normalized;
        await SaveAsync(config, ct);
        return config.Areas;
    }

    /// <inheritdoc />
    public async Task<CadenceAnchors> GetCadenceAnchorsAsync(CancellationToken ct = default)
    {
        var existing = await LoadAsync(ct);
        return ToAnchors(existing);
    }

    /// <inheritdoc />
    public async Task<CadenceAnchors> UpdateCadenceAnchorsAsync(CadenceAnchors anchors, CancellationToken ct = default)
    {
        var normalized = new CadenceAnchors(
            FiscalYearStartMonth: Math.Clamp(anchors.FiscalYearStartMonth, 1, 12),
            WeekStartDay: anchors.WeekStartDay,
            MonthStartDay: Math.Clamp(anchors.MonthStartDay, 1, 28),
            FortnightAnchor: DateTime.SpecifyKind(anchors.FortnightAnchor.Date, DateTimeKind.Utc));

        var config = await LoadOrCreateAsync(ct);
        config.FiscalYearStartMonth = normalized.FiscalYearStartMonth;
        config.WeekStartDay = normalized.WeekStartDay;
        config.MonthStartDay = normalized.MonthStartDay;
        config.FortnightAnchor = normalized.FortnightAnchor;
        await SaveAsync(config, ct);
        return normalized;
    }

    /// <inheritdoc />
    public async Task<IngestionStatus> GetIngestionStatusAsync(CancellationToken ct = default)
    {
        var existing = await LoadAsync(ct);
        return new IngestionStatus(existing?.SubmissionsClosed ?? false, existing?.SubmissionsClosedMessage);
    }

    /// <inheritdoc />
    public async Task<IngestionStatus> UpdateIngestionStatusAsync(bool closed, string? message, CancellationToken ct = default)
    {
        var trimmed = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        var config = await LoadOrCreateAsync(ct);
        config.SubmissionsClosed = closed;
        config.SubmissionsClosedMessage = trimmed;
        await SaveAsync(config, ct);
        return new IngestionStatus(config.SubmissionsClosed, config.SubmissionsClosedMessage);
    }

    /// <inheritdoc />
    public async Task<CadenceWindows> GetCadenceWindowsAsync(CancellationToken ct = default)
    {
        var existing = await LoadAsync(ct);
        return ToWindows(existing);
    }

    /// <inheritdoc />
    public async Task<CadenceWindows> UpdateCadenceWindowsAsync(CadenceWindows windows, CancellationToken ct = default)
    {
        var normalized = new CadenceWindows(
            Daily: Clamp(windows.Daily), Weekly: Clamp(windows.Weekly), Fortnightly: Clamp(windows.Fortnightly),
            Monthly: Clamp(windows.Monthly), Quarterly: Clamp(windows.Quarterly), SemiAnnually: Clamp(windows.SemiAnnually),
            Yearly: Clamp(windows.Yearly));

        var config = await LoadOrCreateAsync(ct);
        config.CadenceWindows = new CadenceWindowSettings
        {
            Daily = ToOverride(normalized.Daily),
            Weekly = ToOverride(normalized.Weekly),
            Fortnightly = ToOverride(normalized.Fortnightly),
            Monthly = ToOverride(normalized.Monthly),
            Quarterly = ToOverride(normalized.Quarterly),
            SemiAnnually = ToOverride(normalized.SemiAnnually),
            Yearly = ToOverride(normalized.Yearly),
        };
        await SaveAsync(config, ct);
        return normalized;
    }

    /// <summary>Sane upper bound for either offset — about a year, in hours.</summary>
    internal const double MaxWindowHours = 24 * 366;

    /// <summary>Clamp both hour values to a sane, non-negative range (0 to <see cref="MaxWindowHours"/>).</summary>
    internal static CadenceWindow Clamp(CadenceWindow w) => new(
        Math.Clamp(w.OpenOffsetHours, 0, MaxWindowHours),
        Math.Clamp(w.GraceHours, 0, MaxWindowHours));

    private static CadenceWindowOverride ToOverride(CadenceWindow w) =>
        new() { OpenOffsetHours = w.OpenOffsetHours, GraceHours = w.GraceHours };

    /// <summary>Resolve the singleton's per-cadence overrides to concrete values (defaults when unset).</summary>
    internal static CadenceWindows ToWindows(AppConfiguration? config)
    {
        var s = config?.CadenceWindows;
        return new CadenceWindows(
            Daily: ToWindow(s?.Daily), Weekly: ToWindow(s?.Weekly), Fortnightly: ToWindow(s?.Fortnightly),
            Monthly: ToWindow(s?.Monthly), Quarterly: ToWindow(s?.Quarterly), SemiAnnually: ToWindow(s?.SemiAnnually),
            Yearly: ToWindow(s?.Yearly));
    }

    private static CadenceWindow ToWindow(CadenceWindowOverride? o) =>
        new(o?.OpenOffsetHours ?? 0, o?.GraceHours ?? 0);

    /// <summary>Resolve the singleton's nullable anchor fields to concrete values (defaults when unset).</summary>
    internal static CadenceAnchors ToAnchors(AppConfiguration? config) => new(
        FiscalYearStartMonth: config?.FiscalYearStartMonth ?? CadenceAnchors.Default.FiscalYearStartMonth,
        WeekStartDay: config?.WeekStartDay ?? CadenceAnchors.Default.WeekStartDay,
        MonthStartDay: config?.MonthStartDay ?? CadenceAnchors.Default.MonthStartDay,
        FortnightAnchor: config?.FortnightAnchor is { } fa
            ? DateTime.SpecifyKind(fa.Date, DateTimeKind.Utc)
            : CadenceAnchors.Default.FortnightAnchor);

    /// <summary>Trim, drop blanks and de-duplicate (case-insensitively) while preserving order.</summary>
    internal static List<string> Normalize(IReadOnlyList<string>? areas)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in areas ?? Array.Empty<string>())
        {
            var trimmed = raw?.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            if (seen.Add(trimmed)) result.Add(trimmed);
        }
        return result;
    }

    private Task<AppConfiguration?> LoadAsync(CancellationToken ct) =>
        _ctx.AppConfiguration.Find(FilterDefinition<AppConfiguration>.Empty).FirstOrDefaultAsync(ct);

    private async Task<AppConfiguration> LoadOrCreateAsync(CancellationToken ct)
    {
        var existing = await LoadAsync(ct);
        if (existing is not null) return existing;
        var now = _audit.UtcNow;
        return new AppConfiguration { CreatedAt = now, CreatedBy = _audit.UserName };
    }

    private async Task SaveAsync(AppConfiguration config, CancellationToken ct)
    {
        config.ModifiedAt = _audit.UtcNow;
        config.ModifiedBy = _audit.UserName;
        await _ctx.AppConfiguration.ReplaceOneAsync(
            Builders<AppConfiguration>.Filter.Eq(c => c.Id, config.Id),
            config,
            new ReplaceOptions { IsUpsert = true },
            ct);
    }
}
