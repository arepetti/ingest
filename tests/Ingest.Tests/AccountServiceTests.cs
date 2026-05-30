using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Services;

namespace Ingest.Tests;

/// <summary>
/// Focused tests for <see cref="AccountService"/> behaviour. The non-trivial paths today are
/// the uniqueness check on create (already exercised through integration tests) and the new
/// delete guard that refuses to soft-delete an account with any live sample on its name.
/// </summary>
public class AccountServiceTests
{
    private static AccountService NewService(out FakeAccountRepo accounts, out FakeSampleRepo samples)
    {
        accounts = new FakeAccountRepo();
        samples = new FakeSampleRepo();
        return new AccountService(accounts, samples);
    }

    private static Account NewAccount(string name = "alpha", string? label = null) => new()
    {
        Name = name,
        Label = label,
        Kind = AccountKind.Application,
        Role = AccountRole.Service,
        Enabled = true,
    };

    // ── Name reuse after soft-delete ────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_succeeds_when_existing_account_with_same_name_is_soft_deleted()
    {
        var svc = NewService(out var accounts, out _);
        var first = await svc.CreateAsync(NewAccount(name: "alpha"));
        await svc.DeleteAsync(first.Id);

        var replacement = await svc.CreateAsync(NewAccount(name: "alpha"));

        Assert.NotEqual(first.Id, replacement.Id);
        Assert.False(replacement.IsDeleted);
        // The tombstone is gone.
        Assert.Null(await accounts.GetByIdAsync(first.Id, includeDeleted: true));
    }

    [Fact]
    public async Task Create_still_rejects_when_a_live_account_with_same_name_exists()
    {
        var svc = NewService(out _, out _);
        await svc.CreateAsync(NewAccount(name: "alpha"));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.CreateAsync(NewAccount(name: "alpha")));
        Assert.Contains("already exists", ex.Message);
    }

    // ── Delete guard ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_refuses_when_account_has_submissions_and_suggests_disabling()
    {
        var svc = NewService(out var accounts, out var samples);
        var created = await svc.CreateAsync(NewAccount());
        samples.AccountsInUse.Add(created.Id);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.DeleteAsync(created.Id));
        Assert.Contains("cannot be deleted", ex.Message);
        Assert.Contains("Disable", ex.Message);

        // The account is still live.
        var after = await accounts.GetByIdAsync(created.Id);
        Assert.NotNull(after);
        Assert.False(after!.IsDeleted);
    }

    [Fact]
    public async Task Delete_soft_deletes_when_account_has_no_submissions()
    {
        var svc = NewService(out var accounts, out _);
        var created = await svc.CreateAsync(NewAccount());

        await svc.DeleteAsync(created.Id);

        var after = await accounts.GetByIdAsync(created.Id, includeDeleted: true);
        Assert.NotNull(after);
        Assert.True(after!.IsDeleted);
    }

    [Fact]
    public async Task Delete_unknown_account_is_a_silent_noop()
    {
        var svc = NewService(out _, out _);
        await svc.DeleteAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task Delete_uses_label_in_the_error_message_when_available()
    {
        var svc = NewService(out _, out var samples);
        var created = await svc.CreateAsync(NewAccount(name: "alpha", label: "Alpha service"));
        samples.AccountsInUse.Add(created.Id);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.DeleteAsync(created.Id));
        Assert.Contains("Alpha service", ex.Message);
        Assert.DoesNotContain("'alpha'", ex.Message);
    }

    // ── In-memory repositories (just enough surface) ────────────────────────────────────────

    private sealed class FakeAccountRepo : IAccountRepository
    {
        private readonly List<Account> _store = new();

        public Task<Account?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default)
        {
            var hit = _store.FirstOrDefault(a => a.Id == id && (includeDeleted || !a.IsDeleted));
            return Task.FromResult(hit);
        }

        public Task<Account?> GetByNameAsync(string name, bool includeDeleted = false, CancellationToken ct = default)
        {
            var hit = _store.FirstOrDefault(a =>
                string.Equals(a.Name, name, StringComparison.Ordinal) && (includeDeleted || !a.IsDeleted));
            return Task.FromResult(hit);
        }

        public Task<PagedResult<Account>> ListAsync(PageRequest request, AccountKind? kind = null, AccountRole? role = null, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<Account>(_store.Where(a => !a.IsDeleted).ToList(), _store.Count, 1, _store.Count));

        public Task AddAsync(Account account, CancellationToken ct = default)
        {
            account.CreatedAt = DateTime.UtcNow;
            account.ModifiedAt = account.CreatedAt;
            _store.Add(account);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Account account, CancellationToken ct = default)
        {
            account.ModifiedAt = DateTime.UtcNow;
            var idx = _store.FindIndex(a => a.Id == account.Id);
            if (idx >= 0) _store[idx] = account;
            return Task.CompletedTask;
        }

        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
        {
            var hit = _store.FirstOrDefault(a => a.Id == id);
            if (hit is not null) { hit.IsDeleted = true; hit.DeletedAt = DateTime.UtcNow; }
            return Task.CompletedTask;
        }

        public Task HardDeleteAsync(Guid id, CancellationToken ct = default)
        {
            _store.RemoveAll(a => a.Id == id);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Stub sample repo that lets tests declare which account ids count as "in use" for the
    /// delete guard. Everything else is no-op / empty — the delete path is the only thing
    /// these tests exercise.
    /// </summary>
    private sealed class FakeSampleRepo : ISampleRepository
    {
        public HashSet<Guid> AccountsInUse { get; } = new();

        public Task<PagedResult<SampleProjection>> QueryAsync(SampleQuery query, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<SampleProjection>(Array.Empty<SampleProjection>(), 0, 1, 0));

        public Task<SampleProjection?> GetLatestAsync(Guid serviceId, string schemaName, string valueName, CancellationToken ct = default) =>
            Task.FromResult<SampleProjection?>(null);

        public Task<IReadOnlyList<SampleProjection>> GetAllForSchemaAsync(string schemaName, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SampleProjection>>(Array.Empty<SampleProjection>());

        public Task ReplaceForSubmissionAsync(Guid submissionId, IEnumerable<SampleProjection> projections, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SoftDeleteForSubmissionAsync(Guid submissionId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<bool> IsSchemaInUseAsync(string schemaName, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<bool> IsAccountInUseAsync(Guid serviceAccountId, CancellationToken ct = default) =>
            Task.FromResult(AccountsInUse.Contains(serviceAccountId));

        public IQueryable<SampleProjection> AsQueryable() =>
            Array.Empty<SampleProjection>().AsQueryable();
    }
}
