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
    public async Task<IReadOnlyList<MissingByCadence>> GetMissingAsync(CancellationToken ct = default)
    {
        // Bucket key is the cadence; value accumulates one row per (service, schema) with at
        // least one missing required value of that cadence in the current window. We track
        // both the missing count and the total so the UI can render "2/3 missing".
        var byCadence = new Dictionary<Cadence, List<MissingSubmissionEntry>>();
        // Cache schema lookups: a "global" schema is visible to every service, so we'd otherwise
        // re-fetch the same row N times. The ListVisibleToAsync call still does the audience
        // filtering for service-scoped schemas; this cache only helps the per-service iteration
        // skip the schema entity allocation cost.
        // Cadence windows are constant for the whole report — compute once.
        var windowByCadence = Enum.GetValues<Cadence>()
            .ToDictionary(c => c, c => CadenceCalculator.BucketFor(c, _audit.UtcNow));

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

                var schemas = await _schemas.ListVisibleToAsync(account.Id, ct);
                foreach (var schema in schemas)
                {
                    if (!schema.Enabled) continue;

                    // Tally missing-vs-total per cadence for this (service, schema) tuple in a
                    // single pass over the schema's values, then flush the per-cadence buckets
                    // that ended up with at least one missing entry.
                    var tally = new Dictionary<Cadence, (int Missing, int Total)>();
                    foreach (var v in schema.Values)
                    {
                        if (!v.Enabled || !v.Required) continue;

                        var (start, end) = windowByCadence[v.Cadence];
                        var latest = await _samples.GetLatestAsync(account.Id, schema.Name, v.Name, ct);
                        var satisfied = latest is not null && latest.Timestamp >= start && latest.Timestamp < end;

                        var prev = tally.TryGetValue(v.Cadence, out var t) ? t : (Missing: 0, Total: 0);
                        tally[v.Cadence] = (prev.Missing + (satisfied ? 0 : 1), prev.Total + 1);
                    }

                    foreach (var (cadence, t) in tally)
                    {
                        if (t.Missing == 0) continue;
                        if (!byCadence.TryGetValue(cadence, out var list))
                        {
                            list = new List<MissingSubmissionEntry>();
                            byCadence[cadence] = list;
                        }
                        list.Add(new MissingSubmissionEntry(
                            account.Id, account.Name, account.Label,
                            schema.Name, schema.Label,
                            t.Missing, t.Total));
                    }
                }
            }

            if (accounts.Items.Count < pageSize) break;
            page++;
        }

        // Sort entries inside each bucket for stable rendering, and cadences in their natural
        // order (daily before weekly before monthly, …).
        return byCadence
            .OrderBy(kvp => (int)kvp.Key)
            .Select(kvp =>
            {
                var (start, end) = windowByCadence[kvp.Key];
                var entries = kvp.Value
                    .OrderBy(e => e.ServiceLabel ?? e.ServiceName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(e => e.SchemaLabel ?? e.SchemaName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return new MissingByCadence(kvp.Key, start, end, entries);
            })
            .ToList();
    }
}
