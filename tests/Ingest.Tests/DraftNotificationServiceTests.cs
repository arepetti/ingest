using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Email;
using Ingest.Infrastructure.Reports;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ingest.Tests;

/// <summary>
/// Tests for <see cref="DraftNotificationService"/> (the Step 6 "draft saved" nudge). Covers the
/// self-gating (email master switch + the draft-saved rule), recipient resolution (service-account
/// contact and/or admin list, deduplicated), and that the rendered email carries the relative
/// edit path the operator pastes after their console host.
/// </summary>
public class DraftNotificationServiceTests
{
    private static readonly Guid ServiceId = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();

    private static Submission Draft() => new()
    {
        Id = Guid.NewGuid(),
        ServiceAccountId = ServiceId,
        ServiceName = "Roads",
        IsDraft = true,
        SubmittedAt = new DateTime(2026, 1, 2, 3, 4, 0, DateTimeKind.Utc),
        Samples = new List<Sample>
        {
            new() { SchemaName = "waste", ValueName = "tonnes", Value = 1.0, Timestamp = DateTime.UtcNow },
            new() { SchemaName = "waste", ValueName = "incidents", Value = 0L, Timestamp = DateTime.UtcNow },
        },
    };

    private static (DraftNotificationService svc, FakeEmails queue, FakeSettings settings, FakeAccounts accounts) Build(
        bool emailEnabled = true,
        NotificationRule? rule = null)
    {
        var settings = new FakeSettings
        {
            Settings = new NotificationSettings
            {
                DraftSaved = rule ?? new NotificationRule { Enabled = true, NotifyServiceAccount = true, NotifyAdminList = false },
                AdminRecipientAccountIds = new List<Guid> { AdminId },
            },
        };
        var accounts = new FakeAccounts();
        accounts.Store.Add(new Account { Id = ServiceId, Name = "roads", Label = "Roads", Email = "roads@example.com" });
        accounts.Store.Add(new Account { Id = AdminId, Name = "ops", Label = "Ops", Email = "ops@example.com" });

        var queue = new FakeEmails();
        var content = new EmailContentBuilder(
            new FakeTemplateService(DefaultEmailTemplates.All.First(t => t.Key == DefaultEmailTemplates.DraftSaved)),
            new FluidReportRenderer());
        var svc = new DraftNotificationService(
            settings, content, queue, accounts,
            Options.Create(new EmailOptions { Enabled = emailEnabled }),
            NullLogger<DraftNotificationService>.Instance);
        return (svc, queue, settings, accounts);
    }

    [Fact]
    public async Task Does_nothing_when_email_disabled()
    {
        var (svc, queue, _, _) = Build(emailEnabled: false);
        await svc.NotifyDraftSavedAsync(Draft());
        Assert.Empty(queue.Store);
    }

    [Fact]
    public async Task Does_nothing_when_rule_disabled()
    {
        var (svc, queue, _, _) = Build(rule: new NotificationRule { Enabled = false, NotifyServiceAccount = true });
        await svc.NotifyDraftSavedAsync(Draft());
        Assert.Empty(queue.Store);
    }

    [Fact]
    public async Task Notifies_service_account_with_rendered_edit_path()
    {
        var (svc, queue, _, _) = Build();
        var draft = Draft();

        await svc.NotifyDraftSavedAsync(draft);

        var msg = Assert.Single(queue.Store);
        Assert.Equal("roads@example.com", msg.ToAddress);
        Assert.Equal("Draft saved — Roads", msg.Subject);
        Assert.Contains($"/submissions/{draft.Id}/edit", msg.TextBody);
        Assert.Equal(ServiceId, msg.RelatedAccountId);
    }

    [Fact]
    public async Task Notifies_admin_list_when_requested()
    {
        var (svc, queue, _, _) = Build(rule: new NotificationRule
        {
            Enabled = true,
            NotifyServiceAccount = false,
            NotifyAdminList = true,
        });

        await svc.NotifyDraftSavedAsync(Draft());

        var msg = Assert.Single(queue.Store);
        Assert.Equal("ops@example.com", msg.ToAddress);
    }

    [Fact]
    public async Task Deduplicates_when_service_and_admin_share_an_address()
    {
        var (svc, queue, _, accounts) = Build(rule: new NotificationRule
        {
            Enabled = true,
            NotifyServiceAccount = true,
            NotifyAdminList = true,
        });
        // Make the admin share the service's email so the two recipient sources collide.
        accounts.Store.Single(a => a.Id == AdminId).Email = "roads@example.com";

        await svc.NotifyDraftSavedAsync(Draft());

        Assert.Single(queue.Store);
    }

    [Fact]
    public async Task Does_nothing_when_no_recipient_has_an_email()
    {
        var (svc, queue, _, accounts) = Build();
        accounts.Store.Single(a => a.Id == ServiceId).Email = null;

        await svc.NotifyDraftSavedAsync(Draft());

        Assert.Empty(queue.Store);
    }

    // ── Fakes ──

    private sealed class FakeSettings : INotificationSettingsService
    {
        public NotificationSettings Settings { get; set; } = new();
        public Task<NotificationSettings> GetAsync(CancellationToken ct = default) => Task.FromResult(Settings);
        public Task<NotificationSettings> UpdateAsync(NotificationSettingsUpdate update, CancellationToken ct = default) =>
            Task.FromResult(Settings);
    }

    private sealed class FakeAccounts : IAccountRepository
    {
        public List<Account> Store { get; } = new();
        public Task<Account?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult(Store.FirstOrDefault(a => a.Id == id && (includeDeleted || !a.IsDeleted)));
        public Task<Account?> GetByNameAsync(string name, bool includeDeleted = false, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Account?> GetByExternalLoginAsync(string provider, string email, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<Account>> ListAsync(PageRequest request, AccountKind? kind = null, AccountRole? role = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(Account account, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(Account account, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task HardDeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeEmails : IEmailQueue
    {
        public List<EmailRequest> Store { get; } = new();
        public Task<Guid> EnqueueAsync(EmailRequest request, CancellationToken ct = default)
        {
            Store.Add(request);
            return Task.FromResult(Guid.NewGuid());
        }
        public Task<IReadOnlyList<EmailMessage>> ListForAccountAsync(Guid accountId, string? email, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<long> HardDeleteForAccountAsync(Guid accountId, string? email, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<long> PurgeProcessedOlderThanAsync(DateTime olderThanUtc, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<EmailMessage>> ListAsync(PageRequest request, EmailStatus? status = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeTemplateService : IEmailTemplateService
    {
        private readonly EmailTemplate _template;
        public FakeTemplateService(EmailTemplate template) => _template = template;
        public Task<EmailTemplate> GetAsync(string key, CancellationToken ct = default) => Task.FromResult(_template);
        public Task SeedDefaultsAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<EmailTemplate>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<EmailTemplate>>(new[] { _template });
        public Task<EmailTemplate> UpdateAsync(string key, EmailTemplateUpdate update, CancellationToken ct = default) =>
            Task.FromResult(_template);
    }
}
