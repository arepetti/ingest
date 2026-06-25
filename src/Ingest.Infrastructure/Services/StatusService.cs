using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Core.Validation;

namespace Ingest.Infrastructure.Services;

/// <summary>
/// Default implementation of <see cref="IStatusService"/>. For each visible schema and value it
/// queries the most recent <see cref="SampleProjection"/> and checks whether its timestamp lies
/// inside the value's current cadence bucket. Disabled schemas/values still appear in the result
/// so callers can decide whether to render them.
/// </summary>
public sealed class StatusService : IStatusService
{
    private readonly ISchemaRepository _schemas;
    private readonly ISampleRepository _samples;
    private readonly IAccountRepository _accounts;
    private readonly IAuditContext _audit;

    /// <summary>Create a new <see cref="StatusService"/>.</summary>
    /// <param name="schemas">Schema repository (for the audience-filtered listing).</param>
    /// <param name="samples">Sample projection repository (for the latest-sample lookup).</param>
    /// <param name="accounts">Account repository (for the name-based resolver).</param>
    /// <param name="audit">Audit context used as the clock.</param>
    public StatusService(
        ISchemaRepository schemas,
        ISampleRepository samples,
        IAccountRepository accounts,
        IAuditContext audit)
    {
        _schemas = schemas;
        _samples = samples;
        _accounts = accounts;
        _audit = audit;
    }

    /// <inheritdoc />
    public async Task<ServiceStatus> GetStatusByServiceNameAsync(string serviceName, string period, CancellationToken ct = default)
    {
        var account = await _accounts.GetByNameAsync(serviceName, ct: ct)
            ?? throw new NotFoundException($"Service '{serviceName}'");
        return await GetStatusAsync(account.Id, period, ct);
    }

    /// <inheritdoc />
    public async Task<ServiceStatus> GetStatusAsync(Guid serviceId, string period, CancellationToken ct = default)
    {
        var account = await _accounts.GetByIdAsync(serviceId, ct: ct)
            ?? throw new NotFoundException($"Service '{serviceId}'");

        var visible = await _schemas.ListVisibleToAsync(serviceId, ct);
        // BucketForPeriod is kept for potential header info but the satisfied check uses each value's own cadence bucket.
        _ = CadenceCalculator.BucketForPeriod(period, _audit.UtcNow);

        var schemaStatuses = new List<SchemaStatus>(visible.Count);
        foreach (var schema in visible)
        {
            var schemaEnabled = schema.Enabled;
            var values = new List<SchemaValueStatus>(schema.Values.Count);

            foreach (var v in schema.Values)
            {
                if (v.IsCalculated) continue;

                var (valueStart, valueEnd) = CadenceCalculator.BucketFor(v.Cadence, _audit.UtcNow);
                SampleProjection? latest = null;
                bool satisfied = false;
                if (schemaEnabled && v.Enabled)
                {
                    latest = await _samples.GetLatestAsync(serviceId, schema.Name, v.Name, ct);
                    satisfied = latest is not null &&
                                latest.Timestamp >= valueStart && latest.Timestamp < valueEnd;
                }

                values.Add(new SchemaValueStatus(
                    v.Name,
                    v.Label,
                    v.Cadence,
                    v.Required,
                    v.Enabled,
                    valueStart,
                    valueEnd,
                    latest?.SubmissionId,
                    latest?.Timestamp,
                    satisfied));
            }

            schemaStatuses.Add(new SchemaStatus(schema.Name, schema.Label, schemaEnabled, values));
        }

        return new ServiceStatus(serviceId, account.Name, period, schemaStatuses);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MissingByCadence>> GetMissingAsync(IReadOnlyCollection<Guid>? allowedServiceIds = null, CancellationToken ct = default)
    {
        var scope = allowedServiceIds is { Count: > 0 } ? new HashSet<Guid>(allowedServiceIds) : null;
        var now = _audit.UtcNow;
        // Cadence windows are constant for the whole report — compute the current and the
        // previous window for every cadence once. Previous windows are contiguous with the
        // current ones (previousEnd == currentStart), which the per-value short-circuit below
        // relies on.
        var currentWindow = Enum.GetValues<Cadence>()
            .ToDictionary(c => c, c => CadenceCalculator.BucketFor(c, now));
        var previousWindow = Enum.GetValues<Cadence>()
            .ToDictionary(c => c, c => CadenceCalculator.PreviousBucketFor(c, now));

        // One row per (service, schema) per cadence with at least one missing required value,
        // tracked separately for the current and previous windows so the dashboard can colour
        // them differently (current = still open, previous = overdue).
        var current = new Dictionary<Cadence, List<MissingSubmissionEntry>>();
        var previous = new Dictionary<Cadence, List<MissingSubmissionEntry>>();

        // Page through service-role accounts so a registry with > 500 services still works.
        int page = 1;
        const int pageSize = 200;
        while (true)
        {
            var accounts = await _accounts.ListAsync(
                new PageRequest(page, pageSize, Sort: "name"),
                role: AccountRole.Service,
                ct: ct);
            if (accounts.Items.Count == 0) break;

            foreach (var account in accounts.Items)
            {
                // Disabled service can't catch up, and including it in a "to-do" list would be
                // misleading — the operator already knows it's off.
                if (!account.Enabled) continue;
                // Respect the caller's assigned-service scope (null = unrestricted).
                if (scope is not null && !scope.Contains(account.Id)) continue;

                var schemas = await _schemas.ListVisibleToAsync(account.Id, ct);
                foreach (var schema in schemas)
                {
                    if (!schema.Enabled) continue;

                    var curTally = new Dictionary<Cadence, (int Missing, int Total)>();
                    var prevTally = new Dictionary<Cadence, (int Missing, int Total)>();
                    foreach (var v in schema.Values)
                    {
                        if (!v.Enabled || !v.Required || v.IsCalculated) continue;

                        var cadence = v.Cadence;
                        var (cs, ce) = currentWindow[cadence];
                        var (ps, pe) = previousWindow[cadence];
                        var latest = await _samples.GetLatestAsync(account.Id, schema.Name, v.Name, ct);

                        // Current window.
                        var curSatisfied = latest is not null && latest.Timestamp >= cs && latest.Timestamp < ce;
                        Accumulate(curTally, cadence, curSatisfied);

                        // Previous window — only meaningful when both the schema and the service
                        // already existed before that window closed; otherwise we'd retroactively
                        // flag data nobody could ever have submitted.
                        if (schema.CreatedAt < pe && account.CreatedAt < pe)
                        {
                            bool prevSatisfied;
                            if (latest is null || latest.Timestamp < ps)
                                prevSatisfied = false;                     // nothing as recent as the previous window
                            else if (latest.Timestamp < pe)
                                prevSatisfied = true;                      // the latest sample sits inside it
                            else
                                // The latest sample is newer (current window or beyond), so we
                                // can't tell from it alone — ask whether anything landed in the
                                // previous window. pe == cs, so this only fires when there IS a
                                // current/newer sample, keeping the extra query rare.
                                prevSatisfied = await _samples.ExistsInWindowAsync(account.Id, schema.Name, v.Name, ps, pe, ct);
                            Accumulate(prevTally, cadence, prevSatisfied);
                        }
                    }

                    Flush(curTally, current, account, schema);
                    Flush(prevTally, previous, account, schema);
                }
            }

            if (accounts.Items.Count < pageSize) break;
            page++;
        }

        // Current-window buckets first (ordered by cadence), then previous-window buckets.
        var result = new List<MissingByCadence>();
        result.AddRange(BuildBuckets(current, currentWindow, MissingPeriodKind.Current));
        result.AddRange(BuildBuckets(previous, previousWindow, MissingPeriodKind.Previous));
        return result;

        static void Accumulate(Dictionary<Cadence, (int Missing, int Total)> tally, Cadence cadence, bool satisfied)
        {
            var prev = tally.TryGetValue(cadence, out var t) ? t : (Missing: 0, Total: 0);
            tally[cadence] = (prev.Missing + (satisfied ? 0 : 1), prev.Total + 1);
        }

        static void Flush(
            Dictionary<Cadence, (int Missing, int Total)> tally,
            Dictionary<Cadence, List<MissingSubmissionEntry>> dest,
            Account account,
            Schema schema)
        {
            foreach (var (cadence, t) in tally)
            {
                if (t.Missing == 0) continue;
                if (!dest.TryGetValue(cadence, out var list))
                {
                    list = new List<MissingSubmissionEntry>();
                    dest[cadence] = list;
                }
                list.Add(new MissingSubmissionEntry(
                    account.Id, account.Name, account.Label,
                    schema.Name, schema.Label,
                    t.Missing, t.Total));
            }
        }

        static IEnumerable<MissingByCadence> BuildBuckets(
            Dictionary<Cadence, List<MissingSubmissionEntry>> src,
            Dictionary<Cadence, (DateTime Start, DateTime End)> windows,
            MissingPeriodKind kind) =>
            src.OrderBy(kvp => (int)kvp.Key)
                .Select(kvp =>
                {
                    var (start, end) = windows[kvp.Key];
                    var entries = SortEntries(kvp.Value);
                    return new MissingByCadence(kvp.Key, start, end, kind, entries);
                });
    }

    /// <inheritdoc />
    public async Task<MissingPeriodReport> GetMissingForPeriodAsync(Cadence cadence, int offset, IReadOnlyCollection<Guid>? allowedServiceIds = null, CancellationToken ct = default)
    {
        var (start, end) = CadenceCalculator.BucketAtOffset(cadence, _audit.UtcNow, offset);
        var entries = await ComputeMissingForWindowAsync(cadence, start, end, null, allowedServiceIds, ct);
        return new MissingPeriodReport(cadence, offset, start, end, SortEntries(entries));
    }

    /// <inheritdoc />
    public async Task<MissingHistory> GetMissingHistoryAsync(Cadence cadence, int periods, Guid? serviceId = null, IReadOnlyCollection<Guid>? allowedServiceIds = null, CancellationToken ct = default)
    {
        periods = Math.Clamp(periods, 1, 52);
        var now = _audit.UtcNow;
        var points = new List<MissingHistoryPoint>(periods);
        // Walk oldest → current so the trend reads left-to-right. Offset 0 is the current window.
        for (int i = periods - 1; i >= 0; i--)
        {
            var offset = -i;
            var (start, end) = CadenceCalculator.BucketAtOffset(cadence, now, offset);
            var entries = await ComputeMissingForWindowAsync(cadence, start, end, serviceId, allowedServiceIds, ct);
            points.Add(new MissingHistoryPoint(offset, start, end, entries.Sum(e => e.MissingRequiredCount)));
        }
        return new MissingHistory(cadence, points);
    }

    /// <summary>
    /// Evaluate every enabled Service-role account against a single cadence and a single window,
    /// returning one entry per (service, schema) tuple short at least one required value. A
    /// schema/service is only considered if it existed before the window closed. Used by the
    /// per-period detail and the trend builder. When <paramref name="serviceId"/> is supplied the
    /// walk is scoped to that single service instead of paging through every one.
    /// </summary>
    private async Task<List<MissingSubmissionEntry>> ComputeMissingForWindowAsync(
        Cadence cadence, DateTime start, DateTime end, Guid? serviceId, IReadOnlyCollection<Guid>? allowedServiceIds, CancellationToken ct)
    {
        var entries = new List<MissingSubmissionEntry>();
        var scope = allowedServiceIds is { Count: > 0 } ? new HashSet<Guid>(allowedServiceIds) : null;

        // Scoped to a single service: resolve it directly and skip the registry-wide paging. A
        // missing, deleted, non-service, not-yet-existing or out-of-scope account yields no entries.
        if (serviceId.HasValue)
        {
            if (scope is not null && !scope.Contains(serviceId.Value)) return entries;
            var only = await _accounts.GetByIdAsync(serviceId.Value, ct: ct);
            if (only is { Role: AccountRole.Service })
                await EvaluateAccountAsync(only, cadence, start, end, entries, ct);
            return entries;
        }

        int page = 1;
        const int pageSize = 200;
        while (true)
        {
            var accounts = await _accounts.ListAsync(
                new PageRequest(page, pageSize, Sort: "name"),
                role: AccountRole.Service,
                ct: ct);
            if (accounts.Items.Count == 0) break;

            foreach (var account in accounts.Items)
            {
                if (scope is not null && !scope.Contains(account.Id)) continue;
                await EvaluateAccountAsync(account, cadence, start, end, entries, ct);
            }

            if (accounts.Items.Count < pageSize) break;
            page++;
        }

        return entries;
    }

    /// <summary>
    /// Evaluate one account against a cadence window, appending a missing-entry per schema that is
    /// short at least one required value. Shared by the global and per-service trend walks.
    /// </summary>
    private async Task EvaluateAccountAsync(
        Account account, Cadence cadence, DateTime start, DateTime end,
        List<MissingSubmissionEntry> entries, CancellationToken ct)
    {
        if (!account.Enabled || account.CreatedAt >= end) return;

        var schemas = await _schemas.ListVisibleToAsync(account.Id, ct);
        foreach (var schema in schemas)
        {
            if (!schema.Enabled || schema.CreatedAt >= end) continue;

            int missing = 0, total = 0;
            foreach (var v in schema.Values)
            {
                if (!v.Enabled || !v.Required || v.IsCalculated || v.Cadence != cadence) continue;
                total++;
                var satisfied = await _samples.ExistsInWindowAsync(account.Id, schema.Name, v.Name, start, end, ct);
                if (!satisfied) missing++;
            }

            if (missing > 0)
                entries.Add(new MissingSubmissionEntry(
                    account.Id, account.Name, account.Label,
                    schema.Name, schema.Label,
                    missing, total));
        }
    }

    private static List<MissingSubmissionEntry> SortEntries(IEnumerable<MissingSubmissionEntry> entries) =>
        entries
            .OrderBy(e => e.ServiceLabel ?? e.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.SchemaLabel ?? e.SchemaName, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
