using Ingest.Core.Abstractions;
using Ingest.Core.Entities;

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
        var serviceRefs = new List<ExploreServiceRef>(serviceNameById.Count);
        foreach (var (id, name) in serviceNameById)
        {
            var acc = await _accounts.GetByIdAsync(id, includeDeleted: true, ct);
            serviceRefs.Add(new ExploreServiceRef(id, acc?.Name ?? name, acc?.Label));
        }
        serviceRefs.Sort((a, b) => string.Compare(
            a.ServiceLabel ?? a.ServiceName, b.ServiceLabel ?? b.ServiceName, StringComparison.OrdinalIgnoreCase));

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

        return new ExploreSeriesResult(
            schema.Name, schema.Label, query.Aggregation, query.From, query.To, serviceRefs, valueSeries);
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
