using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Core.Validation;
using Ingest.Infrastructure.Services;

namespace Ingest.Tests;

/// <summary>
/// Tests for <see cref="ExploreService.GetScorecardAsync"/>: the cross-schema RAG status board.
/// Backed by in-process fakes so the aggregation (enabled-only, banded-only, latest-per-service,
/// classification) is exercised without Mongo.
/// </summary>
public class ExploreServiceTests
{
    private static readonly Guid ServiceA = Guid.NewGuid();
    private static readonly Guid ServiceB = Guid.NewGuid();
    private static readonly Guid ServiceC = Guid.NewGuid();

    private static readonly DateTime P1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime P2 = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

    private static SchemaValue Number(
        string name, double? amberMin, double? greenMin, double? greenMax, double? amberMax,
        Cadence cadence = Cadence.Monthly) => new()
    {
        Name = name, Type = SchemaValueType.Number, Cadence = cadence,
        AmberMin = amberMin, GreenMin = greenMin, GreenMax = greenMax, AmberMax = amberMax,
    };

    private static SampleProjection Row(Guid serviceId, string schema, string value, double number, DateTime period) => new()
    {
        SubmissionId = Guid.NewGuid(),
        ServiceAccountId = serviceId,
        ServiceName = serviceId == ServiceA ? "svc_a" : "svc_b",
        SchemaName = schema,
        ValueName = value,
        NumberValue = number,
        Timestamp = period,
        // Reported a few hours after the measurement, so tests can tell SubmittedAt from Timestamp.
        SubmittedAt = period.AddHours(5),
        PeriodStart = period,
        PeriodEnd = period.AddMonths(1),
    };

    [Fact]
    public async Task Scorecard_aggregates_latest_per_service_for_banded_values_only()
    {
        var waste = new Schema
        {
            Name = "waste", Label = "Waste", IsGlobal = true, Enabled = true,
            Values = new List<SchemaValue>
            {
                Number("recycling", 0, 50, 80, 100),
                Number("trash", null, null, null, null), // no band — excluded
                new() { Name = "note", Type = SchemaValueType.String, AmberMin = 0, AmberMax = 5 }, // non-numeric — excluded
            },
        };
        var disabled = new Schema
        {
            Name = "old", Label = "Old", IsGlobal = true, Enabled = false,
            Values = new List<SchemaValue> { Number("x", 0, null, null, 10) }, // banded but schema disabled — excluded
        };

        var latestA = Row(ServiceA, "waste", "recycling", 60, P2); // latest for A → Green
        var samples = new List<SampleProjection>
        {
            Row(ServiceA, "waste", "recycling", 90, P1), // superseded by the newer period
            latestA,
            Row(ServiceB, "waste", "recycling", 95, P2), // latest for B → Amber
            Row(ServiceA, "waste", "trash", 5, P2),      // value has no band → never surfaces
            Row(ServiceA, "old", "x", 4, P2),            // schema disabled → never surfaces
        };

        var svc = new ExploreService(
            new FakeSchemaRepo(waste, disabled),
            new FakeSampleRepo(samples),
            new FakeAccountRepo());

        var result = await svc.GetScorecardAsync(new ExploreScorecardQuery(null));

        var schema = Assert.Single(result.Schemas);
        Assert.Equal("waste", schema.SchemaName);
        var value = Assert.Single(schema.Values);
        Assert.Equal("recycling", value.ValueName);

        Assert.Equal(2, value.Cells.Count);
        var a = Assert.Single(value.Cells, c => c.ServiceId == ServiceA);
        Assert.Equal(60d, a.Value);
        Assert.Equal(RagStatus.Green, a.Status);
        Assert.Equal(P2, a.PeriodStart);
        Assert.Equal(latestA.SubmissionId, a.SubmissionId);
        Assert.Equal(latestA.SubmittedAt, a.SubmittedAt); // reporting time carried onto the cell

        var b = Assert.Single(value.Cells, c => c.ServiceId == ServiceB);
        Assert.Equal(95d, b.Value);
        Assert.Equal(RagStatus.Amber, b.Status);

        // Service labels resolved from the account repo.
        Assert.Equal(2, result.Services.Count);
        Assert.Contains(result.Services, s => s.ServiceId == ServiceA && s.ServiceLabel == "Alpha");
    }

    [Fact]
    public async Task Scorecard_respects_service_filter()
    {
        var waste = new Schema
        {
            Name = "waste", Label = "Waste", IsGlobal = true, Enabled = true,
            Values = new List<SchemaValue> { Number("recycling", 0, 50, 80, 100) },
        };
        var samples = new List<SampleProjection>
        {
            Row(ServiceA, "waste", "recycling", 60, P2),
            Row(ServiceB, "waste", "recycling", 95, P2),
        };

        var svc = new ExploreService(new FakeSchemaRepo(waste), new FakeSampleRepo(samples), new FakeAccountRepo());

        var result = await svc.GetScorecardAsync(new ExploreScorecardQuery(new[] { ServiceA }));

        var value = Assert.Single(Assert.Single(result.Schemas).Values);
        var cell = Assert.Single(value.Cells);
        Assert.Equal(ServiceA, cell.ServiceId);
    }

    [Fact]
    public async Task Scorecard_is_empty_when_no_schema_has_a_band()
    {
        var plain = new Schema
        {
            Name = "plain", Label = "Plain", IsGlobal = true, Enabled = true,
            Values = new List<SchemaValue> { Number("n", null, null, null, null) },
        };
        var svc = new ExploreService(new FakeSchemaRepo(plain), new FakeSampleRepo(new()), new FakeAccountRepo());

        var result = await svc.GetScorecardAsync(new ExploreScorecardQuery(null));

        Assert.Empty(result.Schemas);
        Assert.Empty(result.Services);
    }

    [Fact]
    public async Task Scorecard_last_period_current_marks_non_reporting_service_as_missing()
    {
        var now = DateTime.UtcNow;
        var (curStart, _) = CadenceCalculator.BucketFor(Cadence.Monthly, now);
        var (prevStart, _) = CadenceCalculator.PreviousBucketFor(Cadence.Monthly, now);

        var waste = new Schema
        {
            Name = "waste", Label = "Waste", IsGlobal = true, Enabled = true,
            Values = new List<SchemaValue> { Number("recycling", 0, 50, 80, 100) },
        };
        var currentA = Row(ServiceA, "waste", "recycling", 60, curStart); // A reported the current period
        var samples = new List<SampleProjection>
        {
            Row(ServiceA, "waste", "recycling", 90, prevStart),
            currentA,
            Row(ServiceB, "waste", "recycling", 95, prevStart), // B only ever reported the previous period
        };

        var svc = new ExploreService(new FakeSchemaRepo(waste), new FakeSampleRepo(samples), new FakeAccountRepo());

        var result = await svc.GetScorecardAsync(
            new ExploreScorecardQuery(null, ScorecardMode.LastPeriod, ScorecardPeriod.Current));

        var value = Assert.Single(Assert.Single(result.Schemas).Values);
        Assert.Equal(2, value.Cells.Count); // both services shown, even though B is missing

        var a = Assert.Single(value.Cells, c => c.ServiceId == ServiceA);
        Assert.Equal(60d, a.Value);
        Assert.Equal(RagStatus.Green, a.Status);
        Assert.Equal(curStart, a.PeriodStart);
        Assert.Equal(currentA.SubmissionId, a.SubmissionId);

        var b = Assert.Single(value.Cells, c => c.ServiceId == ServiceB);
        Assert.Null(b.Status);        // missing → no classification
        Assert.Null(b.Value);
        Assert.Null(b.SubmissionId);  // not clickable
        Assert.Null(b.SubmittedAt);   // nothing was reported
        Assert.Equal(curStart, b.PeriodStart); // anchored to the period it was expected for
    }

    [Fact]
    public async Task Scorecard_last_period_latest_closed_reads_the_previous_period()
    {
        var now = DateTime.UtcNow;
        var (curStart, _) = CadenceCalculator.BucketFor(Cadence.Monthly, now);
        var (prevStart, _) = CadenceCalculator.PreviousBucketFor(Cadence.Monthly, now);

        var waste = new Schema
        {
            Name = "waste", Label = "Waste", IsGlobal = true, Enabled = true,
            Values = new List<SchemaValue> { Number("recycling", 0, 50, 80, 100) },
        };
        var samples = new List<SampleProjection>
        {
            Row(ServiceA, "waste", "recycling", 60, curStart),  // current — ignored by LatestClosed
            Row(ServiceA, "waste", "recycling", 90, prevStart), // closed period → Amber
            Row(ServiceB, "waste", "recycling", 95, prevStart), // closed period → Amber
        };

        var svc = new ExploreService(new FakeSchemaRepo(waste), new FakeSampleRepo(samples), new FakeAccountRepo());

        var result = await svc.GetScorecardAsync(
            new ExploreScorecardQuery(null, ScorecardMode.LastPeriod, ScorecardPeriod.LatestClosed));

        var value = Assert.Single(Assert.Single(result.Schemas).Values);
        Assert.Equal(2, value.Cells.Count);

        var a = Assert.Single(value.Cells, c => c.ServiceId == ServiceA);
        Assert.Equal(90d, a.Value); // read the previous period, not the current one
        Assert.Equal(RagStatus.Amber, a.Status);
        Assert.Equal(prevStart, a.PeriodStart);

        var b = Assert.Single(value.Cells, c => c.ServiceId == ServiceB);
        Assert.Equal(95d, b.Value);
        Assert.Equal(RagStatus.Amber, b.Status);
    }

    [Fact]
    public async Task Scorecard_last_period_shows_banded_schema_with_no_submissions_at_all()
    {
        var waste = new Schema
        {
            Name = "waste", Label = "Waste", IsGlobal = true, Enabled = true,
            Values = new List<SchemaValue> { Number("recycling", 0, 50, 80, 100) },
        };
        // No samples at all — latest-available would hide the whole schema; last-period must not.
        var svc = new ExploreService(new FakeSchemaRepo(waste), new FakeSampleRepo(new()), new FakeAccountRepo());

        var result = await svc.GetScorecardAsync(
            new ExploreScorecardQuery(null, ScorecardMode.LastPeriod, ScorecardPeriod.Current));

        var value = Assert.Single(Assert.Single(result.Schemas).Values);
        Assert.Equal(2, value.Cells.Count); // both applicable services shown…
        Assert.All(value.Cells, c => Assert.Null(c.Status)); // …all as missing (grey)
        Assert.All(value.Cells, c => Assert.Null(c.SubmissionId));
    }

    [Fact]
    public async Task Scorecard_last_period_restricts_cards_to_the_schema_audience()
    {
        // Restricted schema: only ServiceA may report it, so ServiceB must not get a card even if
        // a stray sample exists for it.
        var waste = new Schema
        {
            Name = "waste", Label = "Waste", IsGlobal = false, Enabled = true,
            ServiceIds = new List<Guid> { ServiceA },
            Values = new List<SchemaValue> { Number("recycling", 0, 50, 80, 100) },
        };
        var (curStart, _) = CadenceCalculator.BucketFor(Cadence.Monthly, DateTime.UtcNow);
        var samples = new List<SampleProjection>
        {
            Row(ServiceB, "waste", "recycling", 60, curStart), // not in the audience → excluded
        };

        var svc = new ExploreService(new FakeSchemaRepo(waste), new FakeSampleRepo(samples), new FakeAccountRepo());

        var result = await svc.GetScorecardAsync(
            new ExploreScorecardQuery(null, ScorecardMode.LastPeriod, ScorecardPeriod.Current));

        var value = Assert.Single(Assert.Single(result.Schemas).Values);
        var cell = Assert.Single(value.Cells);
        Assert.Equal(ServiceA, cell.ServiceId); // only the audience member, and it's missing
        Assert.Null(cell.Status);
    }

    // ── Fakes ───────────────────────────────────────────────────────────────────────────────

    private sealed class FakeSchemaRepo(params Schema[] schemas) : ISchemaRepository
    {
        public Task<PagedResult<Schema>> ListAsync(PageRequest request, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<Schema>(schemas, schemas.Length, request.Page, request.PageSize));

        public Task<Schema?> GetByNameAsync(string name, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult(schemas.FirstOrDefault(s => s.Name == name));

        public Task<Schema?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default) => throw new NotImplementedException();

        // Mirror the production audience rule: global schemas reach everyone, restricted ones only their listed services.
        public Task<IReadOnlyList<Schema>> ListVisibleToAsync(Guid serviceId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Schema>>(
                schemas.Where(s => s.IsGlobal || s.ServiceIds.Contains(serviceId)).ToList());

        public Task AddAsync(Schema schema, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateAsync(Schema schema, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task HardDeleteAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeSampleRepo(List<SampleProjection> rows) : ISampleRepository
    {
        public Task<IReadOnlyList<SampleProjection>> GetForExploreAsync(
            string schemaName, IReadOnlyList<string> valueNames, IReadOnlyList<Guid>? serviceIds,
            DateTime? from, DateTime? to, CancellationToken ct = default)
        {
            var names = new HashSet<string>(valueNames, StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<SampleProjection> hits = rows
                .Where(r => r.SchemaName == schemaName && names.Contains(r.ValueName))
                .Where(r => serviceIds is null || serviceIds.Count == 0 || serviceIds.Contains(r.ServiceAccountId))
                .ToList();
            return Task.FromResult(hits);
        }

        public Task<PagedResult<SampleProjection>> QueryAsync(SampleQuery query, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SampleProjection?> GetLatestAsync(Guid serviceId, string schemaName, string valueName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> ExistsInWindowAsync(Guid serviceId, string schemaName, string valueName, DateTime start, DateTime end, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<SampleProjection>> GetAllForSchemaAsync(string schemaName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ReplaceForSubmissionAsync(Guid submissionId, IEnumerable<SampleProjection> projections, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SoftDeleteForSubmissionAsync(Guid submissionId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> IsSchemaInUseAsync(string schemaName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> IsAccountInUseAsync(Guid serviceAccountId, CancellationToken ct = default) => throw new NotImplementedException();
        public IQueryable<SampleProjection> AsQueryable() => throw new NotImplementedException();
        public Task<IReadOnlyList<SampleProjection>> ListByServiceAsync(Guid serviceId, bool includeDeleted = false, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> RedactByServiceAsync(Guid serviceId, string pseudonym, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> HardDeleteByServiceAsync(Guid serviceId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeAccountRepo(params Guid[] serviceIds) : IAccountRepository
    {
        private readonly Guid[] _ids = serviceIds.Length > 0 ? serviceIds : new[] { ServiceA, ServiceB };

        private static Account Acc(Guid id)
        {
            var label = id == ServiceA ? "Alpha" : id == ServiceB ? "Bravo" : id == ServiceC ? "Charlie" : "Other";
            return new Account
            {
                Id = id, Name = label.ToLowerInvariant(), Label = label,
                Kind = AccountKind.Application, Role = AccountRole.Service, Enabled = true,
            };
        }

        public Task<Account?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult<Account?>(Acc(id));

        // One page is enough for the tests; the production paging loop stops as soon as a short page comes back.
        public Task<PagedResult<Account>> ListAsync(PageRequest request, AccountKind? kind = null, AccountRole? role = null, CancellationToken ct = default)
        {
            var items = _ids.Select(Acc).ToList();
            return Task.FromResult(new PagedResult<Account>(items, items.Count, request.Page, request.PageSize));
        }

        public Task<Account?> GetByNameAsync(string name, bool includeDeleted = false, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Account?> GetByExternalLoginAsync(string provider, string email, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AddAsync(Account account, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateAsync(Account account, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task HardDeleteAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
