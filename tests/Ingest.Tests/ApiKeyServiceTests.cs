using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Services;

namespace Ingest.Tests;

/// <summary>
/// Covers <see cref="ApiKeyService.RotateAsync"/>'s optional-expiry rules (future-only, capped at
/// two years, UTC-normalised) and the <see cref="ApiKey.IsActive"/> boundary the authentication
/// handler depends on to reject expired/revoked/deleted keys.
/// </summary>
public class ApiKeyServiceTests
{
    private static readonly DateTime Now = new(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc);

    private static ApiKeyService NewService(out FakeAccountRepo accounts, out FakeApiKeyRepo keys, out Account account)
    {
        accounts = new FakeAccountRepo();
        keys = new FakeApiKeyRepo();
        account = new Account { Name = "robot", Kind = AccountKind.Application, Role = AccountRole.Service, Enabled = true };
        accounts.Seed(account);
        return new ApiKeyService(accounts, keys, new FakeHasher(), new FixedClock(Now), new NoopAuditLogService());
    }

    // ── Expiry: happy paths ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rotate_without_expiry_creates_a_never_expiring_key()
    {
        var svc = NewService(out _, out var keys, out var account);

        var result = await svc.RotateAsync(account.Id);

        Assert.Null(result.Entity.ExpiresAt);
        Assert.Null(Assert.Single(keys.Added).ExpiresAt);
    }

    [Fact]
    public async Task Rotate_with_valid_future_expiry_persists_it_as_utc()
    {
        var svc = NewService(out _, out var keys, out var account);
        var expiry = Now.AddYears(1);

        var result = await svc.RotateAsync(account.Id, expiry);

        Assert.Equal(expiry, result.Entity.ExpiresAt);
        Assert.Equal(DateTimeKind.Utc, result.Entity.ExpiresAt!.Value.Kind);
        Assert.Equal(expiry, Assert.Single(keys.Added).ExpiresAt);
    }

    [Fact]
    public async Task Rotate_accepts_an_expiry_exactly_two_years_out()
    {
        var svc = NewService(out _, out _, out var account);

        var result = await svc.RotateAsync(account.Id, Now.AddYears(ApiKeyService.MaxLifetimeYears));

        Assert.Equal(Now.AddYears(ApiKeyService.MaxLifetimeYears), result.Entity.ExpiresAt);
    }

    [Fact]
    public async Task Rotate_normalises_an_unspecified_kind_expiry_to_utc()
    {
        var svc = NewService(out _, out _, out var account);
        var unspecified = DateTime.SpecifyKind(Now.AddMonths(3), DateTimeKind.Unspecified);

        var result = await svc.RotateAsync(account.Id, unspecified);

        Assert.Equal(DateTimeKind.Utc, result.Entity.ExpiresAt!.Value.Kind);
        Assert.Equal(Now.AddMonths(3), result.Entity.ExpiresAt);
    }

    // ── Expiry: rejection paths ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rotate_rejects_an_expiry_in_the_past()
    {
        var svc = NewService(out _, out var keys, out var account);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.RotateAsync(account.Id, Now.AddDays(-1)));
        Assert.Contains("future", ex.Message);
        Assert.Empty(keys.Added); // nothing persisted on a rejected request
    }

    [Fact]
    public async Task Rotate_rejects_an_expiry_equal_to_now()
    {
        var svc = NewService(out _, out _, out var account);

        await Assert.ThrowsAsync<ValidationException>(() => svc.RotateAsync(account.Id, Now));
    }

    [Fact]
    public async Task Rotate_rejects_an_expiry_more_than_two_years_out()
    {
        var svc = NewService(out _, out var keys, out var account);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.RotateAsync(account.Id, Now.AddYears(2).AddDays(1)));
        Assert.Contains("2 years", ex.Message);
        Assert.Empty(keys.Added);
    }

    [Fact]
    public async Task Rotate_rejects_an_unknown_account()
    {
        var svc = NewService(out _, out _, out _);

        await Assert.ThrowsAsync<NotFoundException>(() => svc.RotateAsync(Guid.NewGuid(), Now.AddDays(10)));
    }

    // ── Description ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rotate_stores_a_trimmed_description()
    {
        var svc = NewService(out _, out var keys, out var account);

        var result = await svc.RotateAsync(account.Id, description: "  holiday cover for Jane  ");

        Assert.Equal("holiday cover for Jane", result.Entity.Description);
        Assert.Equal("holiday cover for Jane", Assert.Single(keys.Added).Description);
    }

    [Fact]
    public async Task Rotate_stores_a_blank_description_as_null()
    {
        var svc = NewService(out _, out _, out var account);

        var result = await svc.RotateAsync(account.Id, description: "   ");

        Assert.Null(result.Entity.Description);
    }

    [Fact]
    public async Task Rotate_rejects_a_description_over_the_length_cap()
    {
        var svc = NewService(out _, out var keys, out var account);
        var tooLong = new string('x', ApiKeyService.MaxDescriptionLength + 1);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.RotateAsync(account.Id, description: tooLong));
        Assert.Contains($"{ApiKeyService.MaxDescriptionLength} characters", ex.Message);
        Assert.Empty(keys.Added);
    }

    [Fact]
    public async Task UpdateDescription_changes_the_note_on_an_existing_key()
    {
        var svc = NewService(out _, out var keys, out var account);
        var created = await svc.RotateAsync(account.Id, description: "temporary");

        var updated = await svc.UpdateDescriptionAsync(account.Id, created.Entity.Id, "  permanent  ");

        Assert.NotNull(updated);
        Assert.Equal("permanent", updated!.Description);
        Assert.Equal("permanent", keys.Added.Single().Description);
    }

    [Fact]
    public async Task UpdateDescription_clears_the_note_when_blank()
    {
        var svc = NewService(out _, out _, out var account);
        var created = await svc.RotateAsync(account.Id, description: "temporary");

        var updated = await svc.UpdateDescriptionAsync(account.Id, created.Entity.Id, "");

        Assert.Null(updated!.Description);
    }

    [Fact]
    public async Task UpdateDescription_returns_null_for_an_unknown_key()
    {
        var svc = NewService(out _, out _, out var account);

        var updated = await svc.UpdateDescriptionAsync(account.Id, Guid.NewGuid(), "note");

        Assert.Null(updated);
    }

    // ── ApiKey.IsActive (the auth-handler gate) ─────────────────────────────────────────────

    [Fact]
    public void IsActive_true_for_a_fresh_key_without_expiry()
    {
        Assert.True(NewKey().IsActive(Now));
    }

    [Fact]
    public void IsActive_true_while_expiry_is_in_the_future()
    {
        Assert.True(NewKey(expiresAt: Now.AddDays(1)).IsActive(Now));
    }

    [Fact]
    public void IsActive_false_once_expiry_has_passed()
    {
        var key = NewKey(expiresAt: Now);
        Assert.False(key.IsActive(Now.AddSeconds(1)));
    }

    [Fact]
    public void IsActive_false_when_revoked_even_if_not_expired()
    {
        Assert.False(NewKey(revokedAt: Now.AddMinutes(-1)).IsActive(Now));
    }

    [Fact]
    public void IsActive_false_when_soft_deleted()
    {
        var key = NewKey();
        key.IsDeleted = true;
        Assert.False(key.IsActive(Now));
    }

    private static ApiKey NewKey(DateTime? expiresAt = null, DateTime? revokedAt = null) => new()
    {
        AccountId = Guid.NewGuid(),
        KeyId = "abc123",
        Hash = "hash",
        Salt = "salt",
        ExpiresAt = expiresAt,
        RevokedAt = revokedAt,
    };

    // ── Fakes ───────────────────────────────────────────────────────────────────────────────

    private sealed class FixedClock(DateTime now) : IAuditContext
    {
        public string? UserName => "test";
        public Guid? AccountId => null;
        public DateTime UtcNow => now;
    }

    private sealed class FakeHasher : IApiKeyHasher
    {
        private int _n;
        public GeneratedApiKey Generate()
        {
            var id = $"key{_n++}";
            return new GeneratedApiKey($"{id}.secret", id, "secret", "salt", "hash");
        }
        public GeneratedApiKey? Import(string plaintext) => throw new NotSupportedException();
        public bool TrySplit(string presented, out string keyId, out string secret) => throw new NotSupportedException();
        public bool Verify(string secret, string storedSalt, string storedHash) => throw new NotSupportedException();
        public string Hash(string secret, string salt) => throw new NotSupportedException();
    }

    private sealed class FakeAccountRepo : IAccountRepository
    {
        private readonly List<Account> _store = new();
        public void Seed(Account a) => _store.Add(a);

        public Task<Account?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult(_store.FirstOrDefault(a => a.Id == id && (includeDeleted || !a.IsDeleted)));

        public Task<Account?> GetByNameAsync(string name, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult(_store.FirstOrDefault(a => a.Name == name));

        public Task<Account?> GetByExternalLoginAsync(string provider, string email, CancellationToken ct = default) =>
            Task.FromResult<Account?>(null);

        public Task<PagedResult<Account>> ListAsync(PageRequest request, AccountKind? kind = null, AccountRole? role = null, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<Account>(_store, _store.Count, 1, _store.Count));

        public Task AddAsync(Account account, CancellationToken ct = default) { _store.Add(account); return Task.CompletedTask; }
        public Task UpdateAsync(Account account, CancellationToken ct = default) => Task.CompletedTask;
        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task HardDeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => Task.FromResult(0L);
    }

    private sealed class FakeApiKeyRepo : IApiKeyRepository
    {
        public List<ApiKey> Added { get; } = new();

        public Task<ApiKey?> GetByKeyIdAsync(string keyId, CancellationToken ct = default) =>
            Task.FromResult(Added.FirstOrDefault(k => k.KeyId == keyId));

        public Task<IReadOnlyList<ApiKey>> GetActiveByAccountAsync(Guid accountId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ApiKey>>(Added.Where(k => k.AccountId == accountId).ToList());

        public Task<IReadOnlyList<ApiKey>> ListByAccountAsync(Guid accountId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ApiKey>>(Added.Where(k => k.AccountId == accountId).ToList());

        public Task AddAsync(ApiKey key, CancellationToken ct = default) { Added.Add(key); return Task.CompletedTask; }
        public Task UpdateAsync(ApiKey key, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> HardDeleteByAccountAsync(Guid accountId, CancellationToken ct = default) => Task.FromResult(0L);
    }
}
