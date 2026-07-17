using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Services;

namespace Ingest.Tests;

public class StatusServiceCalculatedTests
{
    private static readonly DateTime FixedNow = new(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetStatusAsync_omits_calculated_values()
    {
        var serviceId = Guid.NewGuid();
        var account = new Account
        {
            Id = serviceId,
            Name = "roads",
            Kind = AccountKind.Application,
            Role = AccountRole.Service,
            Enabled = true,
        };
        var schema = new Schema
        {
            Name = "kpis",
            Enabled = true,
            IsGlobal = true,
            Values =
            {
                new SchemaValue { Name = "a", Type = SchemaValueType.Number, Cadence = Cadence.Monthly, Required = true },
                new SchemaValue { Name = "total", Type = SchemaValueType.Number, Cadence = Cadence.Monthly, Kind = SchemaValueKind.Calculated, Expression = "a * 2" },
            },
        };

        var svc = new StatusService(
            new SchemaRepo(schema),
            new EmptySampleRepo(),
            new AccountRepo(account),
            new TestClock(FixedNow),
            new FakeAppConfigurationService());

        var status = await svc.GetStatusAsync(serviceId, "current");
        var values = Assert.Single(status.Schemas).Values;
        Assert.Single(values);
        Assert.Equal("a", values[0].ValueName);
    }

    private sealed class AccountRepo(Account account) : IAccountRepository
    {
        public Task<Account?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult<Account?>(account.Id == id ? account : null);
        public Task<Account?> GetByNameAsync(string name, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult<Account?>(account.Name == name ? account : null);
        public Task<Account?> GetByExternalLoginAsync(string provider, string email, CancellationToken ct = default) => Task.FromResult<Account?>(null);
        public Task<PagedResult<Account>> ListAsync(PageRequest request, AccountKind? kind = null, AccountRole? role = null, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<Account>(new[] { account }, 1, 1, 1));
        public Task AddAsync(Account account, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Account account, CancellationToken ct = default) => Task.CompletedTask;
        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task HardDeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => Task.FromResult(0L);
    }

    private sealed class SchemaRepo(Schema schema) : ISchemaRepository
    {
        public Task<Schema?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default) => Task.FromResult<Schema?>(schema);
        public Task<Schema?> GetByNameAsync(string name, bool includeDeleted = false, CancellationToken ct = default) => Task.FromResult<Schema?>(schema);
        public Task<IReadOnlyList<Schema>> ListVisibleToAsync(Guid serviceId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Schema>>(new[] { schema });
        public Task<PagedResult<Schema>> ListAsync(PageRequest request, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<Schema>(new[] { schema }, 1, 1, 1));
        public Task AddAsync(Schema schema, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Schema schema, CancellationToken ct = default) => Task.CompletedTask;
        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task HardDeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => Task.FromResult(0L);
    }

    private sealed class EmptySampleRepo : ISampleRepository
    {
        public Task<SampleProjection?> GetLatestAsync(Guid serviceId, string schemaName, string valueName, CancellationToken ct = default) =>
            Task.FromResult<SampleProjection?>(null);
        public Task<bool> ExistsInWindowAsync(Guid serviceId, string schemaName, string valueName, DateTime start, DateTime end, CancellationToken ct = default) =>
            Task.FromResult(false);
        public Task<PagedResult<SampleProjection>> QueryAsync(SampleQuery query, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<SampleProjection>(Array.Empty<SampleProjection>(), 0, 1, 0));
        public Task<IReadOnlyList<SampleProjection>> GetAllForSchemaAsync(string schemaName, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SampleProjection>>(Array.Empty<SampleProjection>());
        public Task<IReadOnlyList<SampleProjection>> GetForExploreAsync(string schemaName, IReadOnlyList<string> valueNames, IReadOnlyList<Guid>? serviceIds, DateTime? from, DateTime? to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SampleProjection>>(Array.Empty<SampleProjection>());
        public Task ReplaceForSubmissionAsync(Guid submissionId, IEnumerable<SampleProjection> projections, CancellationToken ct = default) => Task.CompletedTask;
        public Task SoftDeleteForSubmissionAsync(Guid submissionId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> IsSchemaInUseAsync(string schemaName, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> IsAccountInUseAsync(Guid serviceAccountId, CancellationToken ct = default) => Task.FromResult(false);
        public IQueryable<SampleProjection> AsQueryable() => Array.Empty<SampleProjection>().AsQueryable();
        public Task<IReadOnlyList<SampleProjection>> ListByServiceAsync(Guid serviceId, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SampleProjection>>(Array.Empty<SampleProjection>());
        public Task<long> RedactByServiceAsync(Guid serviceId, string pseudonym, CancellationToken ct = default) => Task.FromResult(0L);
        public Task<long> HardDeleteByServiceAsync(Guid serviceId, CancellationToken ct = default) => Task.FromResult(0L);
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => Task.FromResult(0L);
    }

    private sealed class TestClock(DateTime now) : IAuditContext
    {
        public DateTime UtcNow => now;
        public string? UserName => "test";
        public Guid? AccountId => null;
    }
}
