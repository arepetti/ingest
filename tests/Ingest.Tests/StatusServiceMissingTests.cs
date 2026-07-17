using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Core.Validation;
using Ingest.Infrastructure.Services;

namespace Ingest.Tests;

/// <summary>
/// Coverage for <see cref="StatusService.GetMissingAsync"/>: ensure the report groups by cadence,
/// skips noise (disabled accounts / schemas / values, optional values, services with no debt), and
/// rolls per-(service, schema) tuples up into <c>n/m</c> counts.
/// </summary>
public class StatusServiceMissingTests
{
    private static readonly DateTime FixedNow = new(2026, 5, 28, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Report_skips_cadences_with_nothing_missing()
    {
        // No samples at all → daily-cadence required value is missing; weekly-cadence optional
        // value is not. The bucket list should contain only the daily entry.
        var svc = BuildService(
            accounts: new[] { Service("alpha", "Alpha", enabled: true) },
            schemas: new[]
            {
                Schema("kpi", values: new[]
                {
                    new SchemaValue { Name = "daily_required", Type = SchemaValueType.Number, Cadence = Cadence.Daily, Required = true },
                    new SchemaValue { Name = "weekly_optional", Type = SchemaValueType.Number, Cadence = Cadence.Weekly, Required = false },
                }),
            },
            samples: Array.Empty<SampleProjection>());

        var report = await svc.GetMissingAsync();
        var bucket = Assert.Single(report);
        Assert.Equal(Cadence.Daily, bucket.Cadence);
        var entry = Assert.Single(bucket.Entries);
        Assert.Equal("alpha", entry.ServiceName);
        Assert.Equal(1, entry.MissingRequiredCount);
        Assert.Equal(1, entry.TotalRequiredCount);
    }

    [Fact]
    public async Task Report_rolls_up_multiple_required_values_in_same_cadence()
    {
        var svc = BuildService(
            accounts: new[] { Service("alpha", "Alpha", enabled: true) },
            schemas: new[]
            {
                Schema("kpi", values: new[]
                {
                    new SchemaValue { Name = "a", Type = SchemaValueType.Number, Cadence = Cadence.Weekly, Required = true },
                    new SchemaValue { Name = "b", Type = SchemaValueType.Number, Cadence = Cadence.Weekly, Required = true },
                    new SchemaValue { Name = "c", Type = SchemaValueType.Number, Cadence = Cadence.Weekly, Required = true },
                }),
            },
            // "a" was submitted inside the current weekly bucket → 2/3 missing, not 3/3.
            samples: new[] { SampleAt("alpha", "kpi", "a", FixedNow.AddMinutes(-30)) });

        var bucket = Assert.Single(await svc.GetMissingAsync());
        Assert.Equal(Cadence.Weekly, bucket.Cadence);
        var entry = Assert.Single(bucket.Entries);
        Assert.Equal(2, entry.MissingRequiredCount);
        Assert.Equal(3, entry.TotalRequiredCount);
    }

    [Fact]
    public async Task Report_ignores_samples_outside_current_window()
    {
        // Sample is older than the weekly bucket (FixedNow - 10 days) → still counts as missing.
        var svc = BuildService(
            accounts: new[] { Service("alpha", "Alpha", enabled: true) },
            schemas: new[]
            {
                Schema("kpi", values: new[]
                {
                    new SchemaValue { Name = "a", Type = SchemaValueType.Number, Cadence = Cadence.Weekly, Required = true },
                }),
            },
            samples: new[] { SampleAt("alpha", "kpi", "a", FixedNow.AddDays(-10)) });

        var bucket = Assert.Single(await svc.GetMissingAsync());
        Assert.Equal(1, bucket.Entries[0].MissingRequiredCount);
    }

    [Fact]
    public async Task Report_skips_disabled_accounts_and_schemas_and_values()
    {
        var svc = BuildService(
            accounts: new[]
            {
                // Disabled account → never appears.
                Service("alpha", "Alpha", enabled: false),
                // Healthy account contributing the only row we expect.
                Service("bravo", "Bravo", enabled: true),
            },
            schemas: new[]
            {
                // Disabled schema → skipped even for healthy account.
                new Schema
                {
                    Name = "disabled_schema",
                    Label = "Disabled Schema",
                    IsGlobal = true,
                    Enabled = false,
                    Values = { new SchemaValue { Name = "x", Type = SchemaValueType.Number, Cadence = Cadence.Daily, Required = true } },
                },
                // Enabled schema with one disabled required value (should be skipped) and one
                // enabled required value (should drive the missing entry).
                Schema("real_schema", values: new[]
                {
                    new SchemaValue { Name = "disabled_val", Type = SchemaValueType.Number, Cadence = Cadence.Daily, Required = true, Enabled = false },
                    new SchemaValue { Name = "real_val", Type = SchemaValueType.Number, Cadence = Cadence.Daily, Required = true },
                }),
            },
            samples: Array.Empty<SampleProjection>());

        var report = await svc.GetMissingAsync();
        var bucket = Assert.Single(report);
        var entry = Assert.Single(bucket.Entries);
        Assert.Equal("bravo", entry.ServiceName);
        Assert.Equal("real_schema", entry.SchemaName);
        // Disabled value is invisible, so the denominator is 1, not 2.
        Assert.Equal(1, entry.TotalRequiredCount);
        Assert.Equal(1, entry.MissingRequiredCount);
    }

    [Fact]
    public async Task Report_returns_empty_when_everything_is_satisfied()
    {
        var svc = BuildService(
            accounts: new[] { Service("alpha", "Alpha", enabled: true) },
            schemas: new[]
            {
                Schema("kpi", values: new[]
                {
                    new SchemaValue { Name = "a", Type = SchemaValueType.Number, Cadence = Cadence.Weekly, Required = true },
                }),
            },
            samples: new[] { SampleAt("alpha", "kpi", "a", FixedNow) });

        Assert.Empty(await svc.GetMissingAsync());
    }

    [Fact]
    public async Task Report_groups_distinct_cadences_into_separate_buckets()
    {
        var svc = BuildService(
            accounts: new[] { Service("alpha", "Alpha", enabled: true) },
            schemas: new[]
            {
                Schema("kpi", values: new[]
                {
                    new SchemaValue { Name = "d", Type = SchemaValueType.Number, Cadence = Cadence.Daily, Required = true },
                    new SchemaValue { Name = "w", Type = SchemaValueType.Number, Cadence = Cadence.Weekly, Required = true },
                    new SchemaValue { Name = "m", Type = SchemaValueType.Number, Cadence = Cadence.Monthly, Required = true },
                }),
            },
            samples: Array.Empty<SampleProjection>());

        var report = await svc.GetMissingAsync();
        Assert.Equal(3, report.Count);
        // Ordered by cadence (Daily=0, Weekly=1, Monthly=2 per the enum).
        Assert.Equal(Cadence.Daily, report[0].Cadence);
        Assert.Equal(Cadence.Weekly, report[1].Cadence);
        Assert.Equal(Cadence.Monthly, report[2].Cadence);
    }

    [Fact]
    public async Task Report_flags_previous_period_for_pre_existing_schema_and_service()
    {
        // Service + schema both predate the previous weekly window and nothing was ever submitted,
        // so the same required value is missing in both the current and the previous window.
        var old = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var svc = BuildService(
            accounts: new[] { Service("alpha", "Alpha", enabled: true, createdAt: old) },
            schemas: new[]
            {
                Schema("kpi", values: new[]
                {
                    new SchemaValue { Name = "a", Type = SchemaValueType.Number, Cadence = Cadence.Weekly, Required = true },
                }, createdAt: old),
            },
            samples: Array.Empty<SampleProjection>());

        var report = await svc.GetMissingAsync();

        var current = Assert.Single(report, b => b.Period == MissingPeriodKind.Current);
        var previous = Assert.Single(report, b => b.Period == MissingPeriodKind.Previous);
        Assert.Equal(Cadence.Weekly, current.Cadence);
        Assert.Equal(Cadence.Weekly, previous.Cadence);
        // Previous window sits immediately before the current one.
        Assert.Equal(current.PeriodStart, previous.PeriodEnd);
        Assert.Equal("alpha", previous.Entries[0].ServiceName);
    }

    [Fact]
    public async Task Report_suppresses_the_previous_period_while_its_grace_is_still_open()
    {
        // Same fixture as Report_flags_previous_period_for_pre_existing_schema_and_service, but a
        // configured Weekly grace big enough to still be open at FixedNow (previous window ends
        // Mon 2026-05-25; +10 days extends it well past FixedNow of 2026-05-28) withholds the
        // previous bucket entirely rather than showing it as overdue.
        var old = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var appConfig = new FakeAppConfigurationService { Windows = CadenceWindows.Default with { Weekly = new CadenceWindow(0, 24 * 10) } };
        var svc = BuildService(
            accounts: new[] { Service("alpha", "Alpha", enabled: true, createdAt: old) },
            schemas: new[]
            {
                Schema("kpi", values: new[]
                {
                    new SchemaValue { Name = "a", Type = SchemaValueType.Number, Cadence = Cadence.Weekly, Required = true },
                }, createdAt: old),
            },
            samples: Array.Empty<SampleProjection>(),
            appConfig: appConfig);

        var report = await svc.GetMissingAsync();

        Assert.Single(report, b => b.Period == MissingPeriodKind.Current);
        Assert.DoesNotContain(report, b => b.Period == MissingPeriodKind.Previous);
    }

    [Fact]
    public async Task Report_shows_the_previous_period_once_its_grace_has_elapsed()
    {
        // Previous window ends Mon 2026-05-25 00:00; a 72h grace pushes the deadline to
        // 2026-05-28 00:00, which is before FixedNow (2026-05-28 10:00) — grace has elapsed, so
        // the previous bucket is reported as overdue same as the zero-grace case.
        var old = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var appConfig = new FakeAppConfigurationService { Windows = CadenceWindows.Default with { Weekly = new CadenceWindow(0, 72) } };
        var svc = BuildService(
            accounts: new[] { Service("alpha", "Alpha", enabled: true, createdAt: old) },
            schemas: new[]
            {
                Schema("kpi", values: new[]
                {
                    new SchemaValue { Name = "a", Type = SchemaValueType.Number, Cadence = Cadence.Weekly, Required = true },
                }, createdAt: old),
            },
            samples: Array.Empty<SampleProjection>(),
            appConfig: appConfig);

        var report = await svc.GetMissingAsync();

        Assert.Single(report, b => b.Period == MissingPeriodKind.Current);
        Assert.Single(report, b => b.Period == MissingPeriodKind.Previous);
    }

    [Fact]
    public async Task GetMissingHistoryAsync_is_unaffected_by_grace()
    {
        // GetMissingHistoryAsync is an explicit historical-by-offset audit view, not a live
        // "is it too late" signal — it must keep reporting a closed offset -1 window as missing
        // even with a large grace configured for the cadence.
        var old = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var appConfig = new FakeAppConfigurationService { Windows = CadenceWindows.Default with { Weekly = new CadenceWindow(0, 24 * 10) } };
        var svc = BuildService(
            accounts: new[] { Service("alpha", "Alpha", enabled: true, createdAt: old) },
            schemas: new[]
            {
                Schema("kpi", values: new[]
                {
                    new SchemaValue { Name = "a", Type = SchemaValueType.Number, Cadence = Cadence.Weekly, Required = true },
                }, createdAt: old),
            },
            samples: Array.Empty<SampleProjection>(),
            appConfig: appConfig);

        var report = await svc.GetMissingForPeriodAsync(Cadence.Weekly, -1);
        Assert.Equal(1, Assert.Single(report.Entries).MissingRequiredCount);
    }

    [Fact]
    public async Task Report_omits_previous_period_for_freshly_created_schema()
    {
        // The schema was created inside the current week, so it never existed during the previous
        // window — it should appear in the current bucket but never in a previous one.
        var old = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var svc = BuildService(
            accounts: new[] { Service("alpha", "Alpha", enabled: true, createdAt: old) },
            schemas: new[]
            {
                Schema("kpi", values: new[]
                {
                    new SchemaValue { Name = "a", Type = SchemaValueType.Number, Cadence = Cadence.Weekly, Required = true },
                }, createdAt: FixedNow.AddDays(-1)),
            },
            samples: Array.Empty<SampleProjection>());

        var report = await svc.GetMissingAsync();

        Assert.Single(report, b => b.Period == MissingPeriodKind.Current);
        Assert.DoesNotContain(report, b => b.Period == MissingPeriodKind.Previous);
    }

    [Fact]
    public async Task Report_treats_previous_period_satisfied_when_sample_lands_in_it()
    {
        // A sample submitted in the previous week satisfies the previous window; the current
        // window is still missing it. Only the current bucket should remain.
        var old = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var svc = BuildService(
            accounts: new[] { Service("alpha", "Alpha", enabled: true, createdAt: old) },
            schemas: new[]
            {
                Schema("kpi", values: new[]
                {
                    new SchemaValue { Name = "a", Type = SchemaValueType.Number, Cadence = Cadence.Weekly, Required = true },
                }, createdAt: old),
            },
            // 2026-05-20 falls in the previous weekly bucket (Mon 2026-05-18 .. Mon 2026-05-25).
            samples: new[] { SampleAt("alpha", "kpi", "a", new DateTime(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc)) });

        var report = await svc.GetMissingAsync();

        Assert.Single(report, b => b.Period == MissingPeriodKind.Current);
        Assert.DoesNotContain(report, b => b.Period == MissingPeriodKind.Previous);
    }

    [Fact]
    public async Task GetMissingForPeriod_returns_entries_for_the_requested_offset()
    {
        var old = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var svc = BuildService(
            accounts: new[] { Service("alpha", "Alpha", enabled: true, createdAt: old) },
            schemas: new[]
            {
                Schema("kpi", values: new[]
                {
                    new SchemaValue { Name = "a", Type = SchemaValueType.Number, Cadence = Cadence.Weekly, Required = true },
                }, createdAt: old),
            },
            samples: Array.Empty<SampleProjection>());

        var report = await svc.GetMissingForPeriodAsync(Cadence.Weekly, -1);

        Assert.Equal(-1, report.Offset);
        var entry = Assert.Single(report.Entries);
        Assert.Equal("alpha", entry.ServiceName);
        Assert.Equal(1, entry.MissingRequiredCount);
    }

    [Fact]
    public async Task GetMissingHistory_returns_oldest_first_ending_with_current()
    {
        var old = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var svc = BuildService(
            accounts: new[] { Service("alpha", "Alpha", enabled: true, createdAt: old) },
            schemas: new[]
            {
                Schema("kpi", values: new[]
                {
                    new SchemaValue { Name = "a", Type = SchemaValueType.Number, Cadence = Cadence.Weekly, Required = true },
                }, createdAt: old),
            },
            samples: Array.Empty<SampleProjection>());

        var history = await svc.GetMissingHistoryAsync(Cadence.Weekly, 3);

        Assert.Equal(Cadence.Weekly, history.Cadence);
        Assert.Equal(3, history.Points.Count);
        // Oldest first, current last.
        Assert.Equal(-2, history.Points[0].Offset);
        Assert.Equal(0, history.Points[2].Offset);
        Assert.All(history.Points, p => Assert.Equal(1, p.TotalMissing));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    private static StatusService BuildService(
        IEnumerable<Account> accounts,
        IEnumerable<Schema> schemas,
        IEnumerable<SampleProjection> samples,
        FakeAppConfigurationService? appConfig = null) =>
        new(new FakeSchemaRepo(schemas), new FakeSampleRepo(samples), new FakeAccountRepo(accounts), new FixedClock(FixedNow), appConfig ?? new FakeAppConfigurationService());

    // CreatedAt defaults to "now" so these fixtures are treated as brand-new: the previous-period
    // guard (CreatedAt < previousPeriodEnd) excludes them, leaving GetMissingAsync to report only
    // the current window. Tests that exercise the previous (overdue) window seed older CreatedAt
    // values explicitly via the optional parameter.
    private static Account Service(string name, string label, bool enabled, DateTime? createdAt = null) => new()
    {
        Name = name,
        Label = label,
        Kind = AccountKind.Application,
        Role = AccountRole.Service,
        Enabled = enabled,
        CreatedAt = createdAt ?? FixedNow,
    };

    private static Schema Schema(string name, IEnumerable<SchemaValue> values, DateTime? createdAt = null) => new()
    {
        Name = name,
        Label = name + " Label",
        IsGlobal = true,
        Enabled = true,
        Values = values.ToList(),
        CreatedAt = createdAt ?? FixedNow,
    };

    private static SampleProjection SampleAt(string serviceName, string schemaName, string valueName, DateTime ts) => new()
    {
        Id = Guid.NewGuid(),
        SubmissionId = Guid.NewGuid(),
        ServiceAccountId = NameToServiceId(serviceName),
        ServiceName = serviceName,
        SchemaName = schemaName,
        ValueName = valueName,
        Timestamp = ts,
    };

    // Same string → same Guid so FakeSampleRepo and FakeAccountRepo agree on which account a
    // sample belongs to without us threading lookups around.
    private static Guid NameToServiceId(string name)
    {
        Span<byte> bytes = stackalloc byte[16];
        var src = System.Text.Encoding.UTF8.GetBytes(name);
        for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(src.Length > i ? src[i] : 0);
        return new Guid(bytes);
    }

    private sealed class FixedClock(DateTime now) : IAuditContext
    {
        public string? UserName => "test";
        public Guid? AccountId => null;
        public DateTime UtcNow => now;
    }

    private sealed class FakeAccountRepo(IEnumerable<Account> seed) : IAccountRepository
    {
        private readonly List<Account> _accounts = seed
            .Select(a => { a.Id = NameToServiceId(a.Name); return a; })
            .ToList();

        public Task<Account?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult(_accounts.FirstOrDefault(a => a.Id == id));

        public Task<Account?> GetByNameAsync(string name, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult(_accounts.FirstOrDefault(a => a.Name == name));

        public Task<Account?> GetByExternalLoginAsync(string provider, string email, CancellationToken ct = default) =>
            Task.FromResult<Account?>(null);

        public Task<PagedResult<Account>> ListAsync(PageRequest request, AccountKind? kind = null, AccountRole? role = null, CancellationToken ct = default)
        {
            var filtered = _accounts
                .Where(a => (!kind.HasValue || a.Kind == kind) && (!role.HasValue || a.Role == role))
                .Skip(request.Skip)
                .Take(request.Take)
                .ToList();
            return Task.FromResult(new PagedResult<Account>(filtered, filtered.Count, request.Page, request.Take));
        }

        public Task AddAsync(Account account, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Account account, CancellationToken ct = default) => Task.CompletedTask;
        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task HardDeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => Task.FromResult(0L);
    }

    private sealed class FakeSchemaRepo(IEnumerable<Schema> seed) : ISchemaRepository
    {
        private readonly List<Schema> _schemas = seed.ToList();

        public Task<Schema?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult(_schemas.FirstOrDefault(s => s.Id == id));

        public Task<Schema?> GetByNameAsync(string name, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult(_schemas.FirstOrDefault(s => s.Name == name));

        public Task<IReadOnlyList<Schema>> ListVisibleToAsync(Guid serviceId, CancellationToken ct = default)
        {
            IReadOnlyList<Schema> hits = _schemas
                .Where(s => !s.IsDeleted && (s.IsGlobal || s.ServiceIds.Contains(serviceId)))
                .ToList();
            return Task.FromResult(hits);
        }

        public Task<PagedResult<Schema>> ListAsync(PageRequest request, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<Schema>(_schemas, _schemas.Count, 1, _schemas.Count));

        public Task AddAsync(Schema schema, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Schema schema, CancellationToken ct = default) => Task.CompletedTask;
        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task HardDeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => Task.FromResult(0L);
    }

    private sealed class FakeSampleRepo(IEnumerable<SampleProjection> seed) : ISampleRepository
    {
        private readonly List<SampleProjection> _samples = seed.ToList();

        public Task<SampleProjection?> GetLatestAsync(Guid serviceId, string schemaName, string valueName, CancellationToken ct = default)
        {
            var hit = _samples
                .Where(s => s.ServiceAccountId == serviceId && s.SchemaName == schemaName && s.ValueName == valueName)
                .OrderByDescending(s => s.Timestamp)
                .FirstOrDefault();
            return Task.FromResult<SampleProjection?>(hit);
        }

        public Task<bool> ExistsInWindowAsync(Guid serviceId, string schemaName, string valueName, DateTime start, DateTime end, CancellationToken ct = default) =>
            Task.FromResult(_samples.Any(s =>
                s.ServiceAccountId == serviceId &&
                s.SchemaName == schemaName &&
                s.ValueName == valueName &&
                s.Timestamp >= start &&
                s.Timestamp < end));

        public Task<PagedResult<SampleProjection>> QueryAsync(SampleQuery query, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<SampleProjection>(Array.Empty<SampleProjection>(), 0, 1, 0));

        public Task<IReadOnlyList<SampleProjection>> GetAllForSchemaAsync(string schemaName, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SampleProjection>>(Array.Empty<SampleProjection>());

        public Task<IReadOnlyList<SampleProjection>> GetForExploreAsync(string schemaName, IReadOnlyList<string> valueNames, IReadOnlyList<Guid>? serviceIds, DateTime? from, DateTime? to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SampleProjection>>(Array.Empty<SampleProjection>());

        public Task ReplaceForSubmissionAsync(Guid submissionId, IEnumerable<SampleProjection> projections, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SoftDeleteForSubmissionAsync(Guid submissionId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<bool> IsSchemaInUseAsync(string schemaName, CancellationToken ct = default) =>
            Task.FromResult(_samples.Any(s => string.Equals(s.SchemaName, schemaName, StringComparison.OrdinalIgnoreCase) && !s.IsDeleted));

        public Task<bool> IsAccountInUseAsync(Guid serviceAccountId, CancellationToken ct = default) =>
            Task.FromResult(_samples.Any(s => s.ServiceAccountId == serviceAccountId && !s.IsDeleted));

        public IQueryable<SampleProjection> AsQueryable() => _samples.AsQueryable();

        public Task<IReadOnlyList<SampleProjection>> ListByServiceAsync(Guid serviceId, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SampleProjection>>(_samples.Where(s => s.ServiceAccountId == serviceId && (includeDeleted || !s.IsDeleted)).ToList());
        public Task<long> RedactByServiceAsync(Guid serviceId, string pseudonym, CancellationToken ct = default) => Task.FromResult(0L);
        public Task<long> HardDeleteByServiceAsync(Guid serviceId, CancellationToken ct = default) => Task.FromResult(0L);
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => Task.FromResult(0L);
    }
}
