using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Core.Security;
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
        return new AccountService(accounts, samples, new NoopAuditLogService());
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

    // ── SSO external-login linking ──────────────────────────────────────────────────────────

    private static Account NewUser(string name) => new()
    {
        Name = name,
        Kind = AccountKind.User,
        Role = AccountRole.Operator,
        Enabled = true,
    };

    [Fact]
    public async Task Create_lowercases_and_keeps_external_logins_for_user_accounts()
    {
        var svc = NewService(out _, out _);
        var user = NewUser("jane");
        user.ExternalLogins.Add(new ExternalLogin { Provider = "Microsoft", Email = "Jane@Example.COM" });

        var created = await svc.CreateAsync(user);

        var link = Assert.Single(created.ExternalLogins);
        Assert.Equal("Microsoft", link.Provider);
        Assert.Equal("jane@example.com", link.Email); // normalised to lower-case
    }

    [Fact]
    public async Task Create_rejects_external_logins_on_application_accounts()
    {
        var svc = NewService(out _, out _);
        var app = NewAccount(name: "robot"); // Application kind
        app.ExternalLogins.Add(new ExternalLogin { Provider = "Google", Email = "robot@example.com" });

        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(app));
        Assert.Contains("User-kind", ex.Message);
        var detail = Assert.Single(ex.ErrorDetails);
        Assert.Equal(DiagnosticCodes.Accounts.SsoLinksUserOnly, detail.Code);
        Assert.Equal(AccountKind.Application.ToString(), detail.Params["accountKind"]);
    }

    [Fact]
    public async Task Create_rejects_a_pair_already_linked_to_another_account()
    {
        var svc = NewService(out _, out _);
        var first = NewUser("jane");
        first.ExternalLogins.Add(new ExternalLogin { Provider = "Microsoft", Email = "shared@example.com" });
        await svc.CreateAsync(first);

        var second = NewUser("john");
        second.ExternalLogins.Add(new ExternalLogin { Provider = "microsoft", Email = "SHARED@example.com" }); // case variants

        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(second));
        Assert.Contains("already linked", ex.Message);
    }

    [Fact]
    public async Task Update_with_null_links_leaves_existing_links_untouched()
    {
        var svc = NewService(out _, out _);
        var user = NewUser("jane");
        user.ExternalLogins.Add(new ExternalLogin { Provider = "Microsoft", Email = "jane@example.com" });
        var created = await svc.CreateAsync(user);

        var updated = await svc.UpdateAsync(created.Id, new AccountUpdate("Jane", null, null, AccountRole.Admin, true, ExternalLogins: null));

        Assert.NotNull(updated);
        Assert.Single(updated!.ExternalLogins);
    }

    [Fact]
    public async Task Update_with_empty_list_clears_links()
    {
        var svc = NewService(out _, out _);
        var user = NewUser("jane");
        user.ExternalLogins.Add(new ExternalLogin { Provider = "Microsoft", Email = "jane@example.com" });
        var created = await svc.CreateAsync(user);

        var updated = await svc.UpdateAsync(created.Id, new AccountUpdate("Jane", null, null, AccountRole.Operator, true, ExternalLogins: Array.Empty<ExternalLogin>()));

        Assert.NotNull(updated);
        Assert.Empty(updated!.ExternalLogins);
    }

    [Fact]
    public async Task Update_preserves_subject_already_bound_to_a_surviving_link()
    {
        var svc = NewService(out var accounts, out _);
        var user = NewUser("jane");
        user.ExternalLogins.Add(new ExternalLogin { Provider = "Microsoft", Email = "jane@example.com" });
        var created = await svc.CreateAsync(user);

        // Simulate a first successful SSO login binding the subject.
        created.ExternalLogins[0].Subject = "sub-123";
        await accounts.UpdateAsync(created);

        // Admin re-saves the same link (no subject on the incoming DTO) — the binding must survive.
        var updated = await svc.UpdateAsync(created.Id, new AccountUpdate("Jane", null, null, AccountRole.Operator, true,
            ExternalLogins: new[] { new ExternalLogin { Provider = "Microsoft", Email = "jane@example.com" } }));

        Assert.Equal("sub-123", updated!.ExternalLogins.Single().Subject);
    }

    [Fact]
    public async Task GetByExternalLogin_matches_case_insensitively_and_skips_deleted()
    {
        var svc = NewService(out var accounts, out _);
        var user = NewUser("jane");
        user.ExternalLogins.Add(new ExternalLogin { Provider = "Microsoft", Email = "jane@example.com" });
        var created = await svc.CreateAsync(user);

        // Case-insensitive provider + email match.
        Assert.NotNull(await accounts.GetByExternalLoginAsync("microsoft", "JANE@EXAMPLE.COM"));

        // Soft-deleted accounts are invisible to the SSO lookup.
        await accounts.SoftDeleteAsync(created.Id);
        Assert.Null(await accounts.GetByExternalLoginAsync("Microsoft", "jane@example.com"));
    }

    // ── SSO sign-in eligibility (the OnTokenValidated rejection matrix) ──────────────────────

    [Fact]
    public void IsEligibleAccount_accepts_a_live_enabled_user()
    {
        var account = new Account { Name = "jane", Kind = AccountKind.User, Enabled = true };
        Assert.True(Ingest.Api.Auth.SsoSignIn.IsEligibleAccount(account));
    }

    [Fact]
    public void IsEligibleAccount_rejects_unknown_disabled_deleted_and_application()
    {
        Assert.False(Ingest.Api.Auth.SsoSignIn.IsEligibleAccount(null)); // unknown identity
        Assert.False(Ingest.Api.Auth.SsoSignIn.IsEligibleAccount(new Account { Name = "j", Kind = AccountKind.User, Enabled = false }));
        Assert.False(Ingest.Api.Auth.SsoSignIn.IsEligibleAccount(new Account { Name = "j", Kind = AccountKind.User, Enabled = true, IsDeleted = true }));
        Assert.False(Ingest.Api.Auth.SsoSignIn.IsEligibleAccount(new Account { Name = "j", Kind = AccountKind.Application, Enabled = true }));
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

    // ── Area (informative-only tag) ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_trims_the_area_and_blanks_it_to_null()
    {
        var svc = NewService(out _, out _);

        var padded = NewAccount(name: "north");
        padded.Area = "  North area  ";
        Assert.Equal("North area", (await svc.CreateAsync(padded)).Area);

        var blank = NewAccount(name: "blank");
        blank.Area = "   ";
        Assert.Null((await svc.CreateAsync(blank)).Area);
    }

    [Fact]
    public async Task Update_trims_the_area_and_blanks_it_to_null()
    {
        var svc = NewService(out _, out _);
        var created = await svc.CreateAsync(NewAccount(name: "svc"));

        var updated = await svc.UpdateAsync(created.Id,
            new AccountUpdate("Svc", null, null, AccountRole.Service, true, Area: "  West  "));
        Assert.Equal("West", updated!.Area);

        var cleared = await svc.UpdateAsync(created.Id,
            new AccountUpdate("Svc", null, null, AccountRole.Service, true, Area: "  "));
        Assert.Null(cleared!.Area);
    }

    // ── Capability overrides (Phase 2) ──────────────────────────────────────────────────────

    [Fact]
    public async Task Create_persists_a_valid_capability_override_set()
    {
        var svc = NewService(out _, out _);
        var op = NewUser("trusted");
        op.Role = AccountRole.Operator;
        op.Capabilities = new List<string> { Capabilities.SchemasManage, Capabilities.SchemasManage }; // dupe

        var created = await svc.CreateAsync(op);

        Assert.Equal(new[] { Capabilities.SchemasManage }, created.Capabilities); // de-duplicated
    }

    [Fact]
    public async Task Create_rejects_unknown_capability_names()
    {
        var svc = NewService(out _, out _);
        var op = NewUser("typo");
        op.Role = AccountRole.Operator;
        op.Capabilities = new List<string> { "schemas:destroy" };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(op));
        Assert.Contains("Unknown capabilities", ex.Message);
        var detail = Assert.Single(ex.ErrorDetails);
        Assert.Equal(DiagnosticCodes.Accounts.UnknownCapabilities, detail.Code);
        Assert.Equal(new[] { "schemas:destroy" }, Assert.IsType<List<string>>(detail.Params["capabilities"]));
    }

    [Fact]
    public async Task Create_normalises_admin_overrides_away()
    {
        var svc = NewService(out _, out _);
        var admin = NewUser("boss");
        admin.Role = AccountRole.Admin;
        admin.Capabilities = new List<string> { Capabilities.StatusRead };

        var created = await svc.CreateAsync(admin);

        // Admins implicitly hold everything; stored overrides are meaningless and cleared.
        Assert.Empty(created.Capabilities);
        Assert.Equal(Capabilities.All.OrderBy(x => x), RoleCapabilities.Effective(created).OrderBy(x => x));
    }

    [Fact]
    public async Task Update_with_null_capabilities_leaves_overrides_untouched()
    {
        var svc = NewService(out _, out _);
        var op = NewUser("trusted");
        op.Role = AccountRole.Operator;
        op.Capabilities = new List<string> { Capabilities.SchemasManage };
        var created = await svc.CreateAsync(op);

        var updated = await svc.UpdateAsync(created.Id,
            new AccountUpdate("Trusted", null, null, AccountRole.Operator, true, Capabilities: null));

        Assert.Equal(new[] { Capabilities.SchemasManage }, updated!.Capabilities);
    }

    [Fact]
    public async Task Update_with_empty_capabilities_reverts_to_role_defaults()
    {
        var svc = NewService(out _, out _);
        var op = NewUser("trusted");
        op.Role = AccountRole.Operator;
        op.Capabilities = new List<string> { Capabilities.SchemasManage };
        var created = await svc.CreateAsync(op);

        var updated = await svc.UpdateAsync(created.Id,
            new AccountUpdate("Trusted", null, null, AccountRole.Operator, true, Capabilities: Array.Empty<string>()));

        // Empty override set => fall back to the role default bundle.
        Assert.Empty(updated!.Capabilities);
        Assert.Equal(RoleCapabilities.DefaultsFor(AccountRole.Operator).OrderBy(x => x),
            RoleCapabilities.Effective(updated).OrderBy(x => x));
    }

    [Fact]
    public async Task Update_to_admin_clears_any_existing_overrides()
    {
        var svc = NewService(out _, out _);
        var op = NewUser("trusted");
        op.Role = AccountRole.Operator;
        op.Capabilities = new List<string> { Capabilities.SchemasManage };
        var created = await svc.CreateAsync(op);

        var updated = await svc.UpdateAsync(created.Id,
            new AccountUpdate("Trusted", null, null, AccountRole.Admin, true, Capabilities: null));

        Assert.Empty(updated!.Capabilities);
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

        public Task<Account?> GetByExternalLoginAsync(string provider, string email, CancellationToken ct = default)
        {
            var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
            var hit = _store.FirstOrDefault(a => !a.IsDeleted && a.ExternalLogins.Any(l =>
                string.Equals(l.Provider, provider, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(l.Email, normalized, StringComparison.OrdinalIgnoreCase)));
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

        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => Task.FromResult(0L);
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

        public Task<bool> ExistsInWindowAsync(Guid serviceId, string schemaName, string valueName, DateTime start, DateTime end, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<SampleProjection>> GetAllForSchemaAsync(string schemaName, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SampleProjection>>(Array.Empty<SampleProjection>());

        public Task<IReadOnlyList<SampleProjection>> GetForExploreAsync(string schemaName, IReadOnlyList<string> valueNames, IReadOnlyList<Guid>? serviceIds, DateTime? from, DateTime? to, CancellationToken ct = default) =>
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

        public Task<IReadOnlyList<SampleProjection>> ListByServiceAsync(Guid serviceId, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SampleProjection>>(Array.Empty<SampleProjection>());
        public Task<long> RedactByServiceAsync(Guid serviceId, string pseudonym, CancellationToken ct = default) => Task.FromResult(0L);
        public Task<long> HardDeleteByServiceAsync(Guid serviceId, CancellationToken ct = default) => Task.FromResult(0L);
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => Task.FromResult(0L);
    }
}
