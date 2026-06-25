using Ingest.Core.Abstractions;
using Ingest.Core.Analytics;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Core.Validation;

namespace Ingest.Infrastructure.Services;

/// <summary>
/// Default <see cref="IExploreService"/>. Loads the live sample projections for a schema (narrowed
/// by value, service and date) and folds them, in memory, into per-cadence buckets with a
/// per-service breakdown. Period boundaries are pre-computed at submission time, so samples from
/// different services in the same cadence window collapse onto the same bucket automatically.
/// </summary>
public sealed class ExploreService : IExploreService
{
    private readonly ISchemaRepository _schemas;
    private readonly ISampleRepository _samples;
    private readonly IAccountRepository _accounts;

    /// <summary>Create a new <see cref="ExploreService"/>.</summary>
    /// <param name="schemas">Schema repository — resolves the value definitions (type, cadence, unit, label).</param>
    /// <param name="samples">Sample projection repository — supplies the rows to aggregate.</param>
    /// <param name="accounts">Account repository — resolves friendly service labels for the breakdown.</param>
    public ExploreService(ISchemaRepository schemas, ISampleRepository samples, IAccountRepository accounts)
    {
        _schemas = schemas;
        _samples = samples;
        _accounts = accounts;
    }

    /// <inheritdoc />
    public async Task<ExploreSeriesResult?> GetSeriesAsync(ExploreSeriesQuery query, CancellationToken ct = default)
    {
        var schema = await _schemas.GetByNameAsync(query.SchemaName, ct: ct);
        if (schema is null) return null;

        // Only numeric values can be reduced; everything else is silently dropped from the scope.
        var numericValues = schema.Values
            .Where(v => v.Type is SchemaValueType.Number or SchemaValueType.Integer)
            .ToList();

        // Intersect the requested names (when any) with the numeric set, preserving the schema's
        // declaration order so the UI shows values the way the author laid them out.
        IReadOnlyList<SchemaValue> scoped;
        if (query.ValueNames is { Count: > 0 })
        {
            var wanted = new HashSet<string>(query.ValueNames, StringComparer.OrdinalIgnoreCase);
            scoped = numericValues.Where(v => wanted.Contains(v.Name)).ToList();
        }
        else
        {
            scoped = numericValues;
        }

        if (scoped.Count == 0)
            return new ExploreSeriesResult(
                schema.Name, schema.Label, query.Aggregation, query.From, query.To,
                Array.Empty<ExploreServiceRef>(), Array.Empty<ExploreValueSeries>());

        var scopedNames = scoped.Select(v => v.Name).ToList();
        var rows = await _samples.GetForExploreAsync(
            schema.Name, scopedNames, query.ServiceIds, query.From, query.To, ct);

        // Resolve labels for the services that actually appear, newest snapshot name as a fallback.
        var serviceNameById = rows
            .GroupBy(r => r.ServiceAccountId)
            .ToDictionary(g => g.Key, g => g.Last().ServiceName);
        var serviceRefs = await ResolveServiceRefsAsync(serviceNameById, ct);

        var rowsByValue = rows
            .Where(r => Numeric(r).HasValue)
            .ToLookup(r => r.ValueName, StringComparer.OrdinalIgnoreCase);

        var valueSeries = scoped.Select(v =>
        {
            var buckets = rowsByValue[v.Name]
                .GroupBy(r => (r.PeriodStart, r.PeriodEnd))
                .OrderBy(g => g.Key.PeriodStart)
                .Select(bucket =>
                {
                    var perService = bucket
                        .GroupBy(r => r.ServiceAccountId)
                        .Select(g => new ExploreServicePoint(
                            g.Key,
                            Reduce(g.Select(r => Numeric(r)!.Value), query.Aggregation),
                            g.Count()))
                        .OrderBy(p => p.ServiceId)
                        .ToList();

                    var all = bucket.Select(r => Numeric(r)!.Value).ToList();
                    return new ExploreBucket(
                        bucket.Key.PeriodStart,
                        bucket.Key.PeriodEnd,
                        Reduce(all, query.Aggregation),
                        all.Count,
                        perService);
                })
                .ToList();

            return new ExploreValueSeries(v.Name, v.Label, v.Type, v.Cadence, v.Unit, buckets);
        }).ToList();

        // Anomaly scoring is opt-in and purely a view aid: it adds a z-score + flag to each point
        // without changing the reduced values. Done as a post-pass over the already-bucketed series.
        if (query.Anomaly)
        {
            var window = AnomalyDetector.ClampWindow(query.AnomalyWindow);
            var threshold = AnomalyDetector.ClampThreshold(query.AnomalyThreshold);
            valueSeries = valueSeries
                .Select(v => ScoreSeriesAnomalies(v, window, threshold, query.AnomalyRobust))
                .ToList();
        }

        return new ExploreSeriesResult(
            schema.Name, schema.Label, query.Aggregation, query.From, query.To, serviceRefs, valueSeries);
    }

    /// <summary>
    /// Walk a value's chronological buckets and score each one against the values that precede it —
    /// the overall (combined) line and every per-service line independently. A gap (a period a
    /// service didn't report) is simply absent from that line's history, never treated as a zero.
    /// Returns a new series with the anomaly fields populated; the reduced values are untouched.
    /// </summary>
    private static ExploreValueSeries ScoreSeriesAnomalies(
        ExploreValueSeries series, int window, double threshold, bool robust)
    {
        var overallHistory = new List<double>();
        var serviceHistory = new Dictionary<Guid, List<double>>();
        var newBuckets = new List<ExploreBucket>(series.Buckets.Count);

        foreach (var b in series.Buckets)
        {
            var (oz, oAnom) = AnomalyDetector.Score(Tail(overallHistory, window), b.Value, threshold, robust);

            var newServices = new List<ExploreServicePoint>(b.Services.Count);
            foreach (var sp in b.Services)
            {
                if (!serviceHistory.TryGetValue(sp.ServiceId, out var hist))
                    serviceHistory[sp.ServiceId] = hist = new List<double>();
                var (sz, sAnom) = AnomalyDetector.Score(Tail(hist, window), sp.Value, threshold, robust);
                newServices.Add(sp with { Z = sz, IsAnomaly = sAnom });
                hist.Add(sp.Value);
            }

            newBuckets.Add(b with { Z = oz, IsAnomaly = oAnom, Services = newServices });
            overallHistory.Add(b.Value);
        }

        return series with { Buckets = newBuckets };
    }

    /// <summary>The last <paramref name="window"/> items of <paramref name="history"/> (or all of them when shorter).</summary>
    private static IReadOnlyList<double> Tail(List<double> history, int window) =>
        history.Count <= window ? history : history.GetRange(history.Count - window, window);

    /// <inheritdoc />
    public async Task<ExploreAnomalyResult> GetAnomaliesAsync(ExploreAnomalyQuery query, CancellationToken ct = default)
    {
        var window = AnomalyDetector.ClampWindow(query.Window);
        var threshold = AnomalyDetector.ClampThreshold(query.Threshold);

        // Resolve the schemas to scan: the requested names, or every schema when none were given.
        var allSchemas = new List<Schema>();
        for (var pageNo = 1; ; pageNo++)
        {
            var page = await _schemas.ListAsync(new PageRequest(pageNo, SchemaPageSize), ct);
            allSchemas.AddRange(page.Items);
            if (page.Items.Count < SchemaPageSize || allSchemas.Count >= page.Total) break;
        }
        if (query.SchemaNames is { Count: > 0 })
        {
            var wanted = new HashSet<string>(query.SchemaNames, StringComparer.OrdinalIgnoreCase);
            allSchemas = allSchemas.Where(s => wanted.Contains(s.Name)).ToList();
        }

        var nowUtc = DateTime.UtcNow;
        // The board shows every service a schema applies to (so non-reporters surface as "missing"),
        // using the same audience rule as the missing-submissions dashboard and the scorecard.
        var expectedBySchema = await BuildExpectedServicesAsync(query.ServiceIds, ct);
        var serviceNameById = new Dictionary<Guid, string>();
        var schemasOut = new List<ExploreAnomalySchema>();

        foreach (var schema in allSchemas)
        {
            if (!schema.Enabled) continue;

            var numeric = schema.Values
                .Where(v => v.Enabled && v.Type is SchemaValueType.Number or SchemaValueType.Integer)
                .ToList();
            if (numeric.Count == 0) continue;

            var expected = expectedBySchema.GetValueOrDefault(schema.Name) ?? new List<Account>();
            if (expected.Count == 0) continue;
            var expectedIds = expected.Select(a => a.Id).ToList();

            var rows = await _samples.GetForExploreAsync(
                schema.Name, numeric.Select(v => v.Name).ToList(), query.ServiceIds, from: null, to: null, ct);
            var rowsByValue = rows
                .Where(r => Numeric(r).HasValue)
                .ToLookup(r => r.ValueName, StringComparer.OrdinalIgnoreCase);

            var valuesOut = new List<ExploreAnomalyValue>();
            foreach (var v in numeric)
            {
                var (start, end) = query.Period == ScorecardPeriod.LatestClosed
                    ? CadenceCalculator.PreviousBucketFor(v.Cadence, nowUtc)
                    : CadenceCalculator.BucketFor(v.Cadence, nowUtc);

                // Collapse to one value per (service, period): newest measurement wins, matching the
                // scorecard. Then each service has a clean chronological series to score against.
                var perServicePeriods = rowsByValue[v.Name]
                    .GroupBy(r => r.ServiceAccountId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.GroupBy(r => r.PeriodStart)
                              .Select(pg =>
                              {
                                  var pick = pg.OrderByDescending(r => r.Timestamp).First();
                                  return (Period: pg.Key, Value: Numeric(pick)!.Value, pick.SubmissionId);
                              })
                              .OrderBy(x => x.Period)
                              .ToList());

                var cells = expectedIds.Select(sid =>
                {
                    if (!perServicePeriods.TryGetValue(sid, out var periods))
                        return new ExploreAnomalyCell(sid, null, null, null, null, start, end);

                    var targetIdx = periods.FindIndex(p => p.Period == start);
                    if (targetIdx < 0)
                        return new ExploreAnomalyCell(sid, null, null, null, null, start, end);

                    var target = periods[targetIdx];
                    var prior = periods.Where(p => p.Period < start).Select(p => p.Value).ToList();
                    var (z, isAnom) = AnomalyDetector.Score(Tail(prior, window), target.Value, threshold, query.Robust);
                    return new ExploreAnomalyCell(
                        sid, target.SubmissionId, target.Value, z,
                        isAnom ? AnomalyState.Anomaly : AnomalyState.Normal, start, end);
                }).OrderBy(c => c.ServiceId).ToList();

                valuesOut.Add(new ExploreAnomalyValue(v.Name, v.Label, v.Unit, v.Cadence, cells));
            }

            if (valuesOut.Count == 0) continue;
            foreach (var acc in expected) serviceNameById.TryAdd(acc.Id, acc.Name);
            schemasOut.Add(new ExploreAnomalySchema(schema.Name, schema.Label, valuesOut));
        }

        var serviceRefs = await ResolveServiceRefsAsync(serviceNameById, ct);
        return new ExploreAnomalyResult(serviceRefs, schemasOut);
    }

    /// <inheritdoc />
    public async Task<ExploreScorecardResult> GetScorecardAsync(ExploreScorecardQuery query, CancellationToken ct = default)
    {
        // Page through every schema (the repo clamps page size to 500). They come back sorted by
        // label then name, so the scorecard's schema order is stable.
        var allSchemas = new List<Schema>();
        for (var pageNo = 1; ; pageNo++)
        {
            var page = await _schemas.ListAsync(new PageRequest(pageNo, SchemaPageSize), ct);
            allSchemas.AddRange(page.Items);
            if (page.Items.Count < SchemaPageSize || allSchemas.Count >= page.Total) break;
        }

        var nowUtc = DateTime.UtcNow;
        var scorecardSchemas = new List<ExploreScorecardSchema>();
        // One shared map so a service's label is resolved once even if it reports across schemas.
        var serviceNameById = new Dictionary<Guid, string>();

        // In last-period mode the board shows every service a schema applies to (so non-reporters
        // surface as "missing"), so we resolve the expected audience per schema up front using the
        // same visibility rule as the missing-submissions dashboard.
        var expectedBySchema = query.Mode == ScorecardMode.LastPeriod
            ? await BuildExpectedServicesAsync(query.ServiceIds, ct)
            : null;

        foreach (var schema in allSchemas)
        {
            if (!schema.Enabled) continue;

            // Only currently-collected, banded numeric values belong on an at-a-glance status board.
            var banded = schema.Values
                .Where(v => v.Enabled && v.Type is SchemaValueType.Number or SchemaValueType.Integer && v.HasTargetBand)
                .ToList();
            if (banded.Count == 0) continue;

            var rows = await _samples.GetForExploreAsync(
                schema.Name, banded.Select(v => v.Name).ToList(), query.ServiceIds, from: null, to: null, ct);

            var rowsByValue = rows
                .Where(r => Numeric(r).HasValue)
                .ToLookup(r => r.ValueName, StringComparer.OrdinalIgnoreCase);

            // Expected audience for this schema (last-period mode only); empty in latest-available.
            var expected = expectedBySchema is null
                ? new List<Account>()
                : expectedBySchema.GetValueOrDefault(schema.Name) ?? new List<Account>();
            var expectedIds = expected.Select(a => a.Id).ToList();

            var valueCards = new List<ExploreScorecardValue>();
            foreach (var v in banded)
            {
                var cells = query.Mode == ScorecardMode.LastPeriod
                    ? LastPeriodCells(rowsByValue[v.Name], expectedIds, v, query.Period, nowUtc)
                    : LatestAvailableCells(rowsByValue[v.Name], v);

                if (cells.Count == 0) continue;
                valueCards.Add(new ExploreScorecardValue(
                    v.Name, v.Label, v.Unit, v.Cadence,
                    v.AmberMin, v.GreenMin, v.GreenMax, v.AmberMax, cells));
            }

            if (valueCards.Count == 0) continue;

            // Resolve labels for every service that appears: the expected audience in last-period
            // mode (present and missing alike), or just the reporters in latest-available mode.
            if (query.Mode == ScorecardMode.LastPeriod)
                foreach (var acc in expected) serviceNameById.TryAdd(acc.Id, acc.Name);
            else
                foreach (var r in rows) serviceNameById.TryAdd(r.ServiceAccountId, r.ServiceName);

            scorecardSchemas.Add(new ExploreScorecardSchema(schema.Name, schema.Label, valueCards));
        }

        var serviceRefs = await ResolveServiceRefsAsync(serviceNameById, ct);
        return new ExploreScorecardResult(serviceRefs, scorecardSchemas);
    }

    /// <summary>Page size used to sweep schemas for the scorecard; matches the repository's clamp.</summary>
    private const int SchemaPageSize = 500;

    /// <summary>
    /// "Latest available" cells: each service's most recent sample for the value (newest period
    /// wins, ties broken on the measurement timestamp). Services that never reported are absent.
    /// </summary>
    private static List<ExploreScorecardCell> LatestAvailableCells(IEnumerable<SampleProjection> rows, SchemaValue v) =>
        rows
            .GroupBy(r => r.ServiceAccountId)
            .Select(g => g.OrderByDescending(r => r.PeriodStart).ThenByDescending(r => r.Timestamp).First())
            .Select(r =>
            {
                var value = Numeric(r)!.Value;
                return new ExploreScorecardCell(
                    r.ServiceAccountId, r.SubmissionId, value, v.ClassifyRag(value),
                    r.PeriodStart, r.PeriodEnd, r.SubmittedAt);
            })
            .OrderBy(c => c.ServiceId)
            .ToList();

    /// <summary>
    /// "Last period" cells: for the one target period, one cell per service the schema applies to
    /// (<paramref name="expectedServiceIds"/>) — a classified one if it submitted that period,
    /// otherwise a "missing" (null status/value) cell anchored to the target period. With no
    /// expected services the value contributes nothing and is dropped by the caller.
    /// </summary>
    private static List<ExploreScorecardCell> LastPeriodCells(
        IEnumerable<SampleProjection> rows, IEnumerable<Guid> expectedServiceIds,
        SchemaValue v, ScorecardPeriod period, DateTime nowUtc)
    {
        var (start, end) = period == ScorecardPeriod.LatestClosed
            ? CadenceCalculator.PreviousBucketFor(v.Cadence, nowUtc)
            : CadenceCalculator.BucketFor(v.Cadence, nowUtc);

        var rowsByService = rows
            .Where(r => r.PeriodStart == start)
            .GroupBy(r => r.ServiceAccountId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Timestamp).First());

        return expectedServiceIds
            .Select(sid =>
            {
                if (!rowsByService.TryGetValue(sid, out var hit))
                    return new ExploreScorecardCell(sid, null, null, null, start, end, null);
                var value = Numeric(hit)!.Value;
                return new ExploreScorecardCell(
                    sid, hit.SubmissionId, value, v.ClassifyRag(value), start, end, hit.SubmittedAt);
            })
            .OrderBy(c => c.ServiceId)
            .ToList();
    }

    /// <summary>
    /// Map every enabled schema to the enabled Service-role accounts it applies to, using the same
    /// audience rule as the missing-submissions dashboard (<see cref="ISchemaRepository.ListVisibleToAsync"/>:
    /// global schemas reach every service, restricted ones only their listed services). Honours the
    /// <paramref name="serviceIds"/> filter so the board can be scoped to a team. Keyed by schema name.
    /// </summary>
    private async Task<Dictionary<string, List<Account>>> BuildExpectedServicesAsync(
        IReadOnlyList<Guid>? serviceIds, CancellationToken ct)
    {
        var filter = serviceIds is { Count: > 0 } ? new HashSet<Guid>(serviceIds) : null;
        var bySchema = new Dictionary<string, List<Account>>(StringComparer.OrdinalIgnoreCase);

        // Page through service accounts so a registry with > 500 services still works.
        for (int page = 1; ; page++)
        {
            var accounts = await _accounts.ListAsync(
                new PageRequest(page, 200, Sort: "name"), role: AccountRole.Service, ct: ct);
            if (accounts.Items.Count == 0) break;

            foreach (var account in accounts.Items)
            {
                // A disabled service can't report, so listing it as "missing" would be misleading.
                if (!account.Enabled) continue;
                if (filter is not null && !filter.Contains(account.Id)) continue;

                foreach (var schema in await _schemas.ListVisibleToAsync(account.Id, ct))
                {
                    if (!bySchema.TryGetValue(schema.Name, out var list))
                        bySchema[schema.Name] = list = new List<Account>();
                    list.Add(account);
                }
            }

            if (accounts.Items.Count < 200) break;
        }

        return bySchema;
    }

    /// <summary>
    /// Resolve friendly service labels for the ids that appear in a result, falling back to the
    /// machine name snapshotted on the samples when the account can't be loaded. Returns the refs
    /// sorted by label (then name) so the UI lists services consistently.
    /// </summary>
    private async Task<List<ExploreServiceRef>> ResolveServiceRefsAsync(
        IReadOnlyDictionary<Guid, string> serviceNameById, CancellationToken ct)
    {
        var refs = new List<ExploreServiceRef>(serviceNameById.Count);
        foreach (var (id, name) in serviceNameById)
        {
            var acc = await _accounts.GetByIdAsync(id, includeDeleted: true, ct);
            refs.Add(new ExploreServiceRef(id, acc?.Name ?? name, acc?.Label));
        }
        refs.Sort((a, b) => string.Compare(
            a.ServiceLabel ?? a.ServiceName, b.ServiceLabel ?? b.ServiceName, StringComparison.OrdinalIgnoreCase));
        return refs;
    }

    /// <summary>Pull a numeric value out of a projection regardless of whether it was a Number or an Integer.</summary>
    private static double? Numeric(SampleProjection s) => s.NumberValue ?? (double?)s.IntegerValue;

    /// <summary>Reduce a set of samples to a single number per the requested aggregation.</summary>
    private static double Reduce(IEnumerable<double> values, ExploreAggregation agg)
    {
        // Materialise once: every branch needs more than a single pass or the count.
        var list = values as IReadOnlyCollection<double> ?? values.ToList();
        if (list.Count == 0) return 0d;
        return agg switch
        {
            ExploreAggregation.Sum => list.Sum(),
            ExploreAggregation.Min => list.Min(),
            ExploreAggregation.Max => list.Max(),
            ExploreAggregation.Count => list.Count,
            _ => list.Average(),
        };
    }
}
