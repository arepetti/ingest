using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
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

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    private static StatusService BuildService(
        IEnumerable<Account> accounts,
        IEnumerable<Schema> schemas,
        IEnumerable<SampleProjection> samples) =>
        new(new FakeSchemaRepo(schemas), new FakeSampleRepo(samples), new FakeAccountRepo(accounts), new FixedClock(FixedNow));

    private static Account Service(string name, string label, bool enabled) => new()
    {
        Name = name,
        Label = label,
        Kind = AccountKind.Application,
        Role = AccountRole.Service,
        Enabled = enabled,
    };

    private static Schema Schema(string name, IEnumerable<SchemaValue> values) => new()
    {
        Name = name,
        Label = name + " Label",
        IsGlobal = true,
        Enabled = true,
        Values = values.ToList(),
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

        public Task<PagedResult<SampleProjection>> QueryAsync(SampleQuery query, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<SampleProjection>(Array.Empty<SampleProjection>(), 0, 1, 0));

        public Task<IReadOnlyList<SampleProjection>> GetAllForSchemaAsync(string schemaName, CancellationToken ct = default) =>
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
    }
}
