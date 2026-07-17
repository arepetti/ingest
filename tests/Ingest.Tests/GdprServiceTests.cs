using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Retention;
using Ingest.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace Ingest.Tests;

/// <summary>
/// Covers the three GDPR data-rights services: <see cref="ErasureService"/> (anonymise vs delete),
/// <see cref="RetentionService"/> (per-target purge cutoffs), and <see cref="PersonalDataService"/>
/// (DSAR bundle completeness, including the outbox emails the registry backup omits).
/// </summary>
public class GdprServiceTests
{
    private static readonly DateTime Now = new(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc);

    // ── Erasure: anonymise ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Anonymise_strips_identity_but_keeps_numeric_values()
    {
        var ctx = new Ctx();
        var account = ctx.SeedAccount();
        ctx.ApiKeys.Store.Add(new ApiKey { AccountId = account.Id, KeyId = "k1", Hash = "h", Salt = "s" });
        var submission = ctx.SeedSubmission(account.Id);
        var stringSample = ctx.SeedSample(account.Id, SchemaValueType.String, s => s.StringValue = "Jane Doe");
        var numberSample = ctx.SeedSample(account.Id, SchemaValueType.Number, s => s.NumberValue = 42);
        ctx.Emails.Store.Add(new EmailMessage { ToAddress = "jane@example.com", Subject = "hi", TextBody = "b", RelatedAccountId = account.Id });
        ctx.AuditLogs.Store.Add(new AuditLog { TargetId = account.Id, TargetName = "jane", Timestamp = Now });
        ctx.NotificationLogs.Store.Add(new NotificationLog { Key = $"upcoming:{account.Id}:s:v:p", CreatedAt = Now });

        var result = await ctx.Erasure().EraseAccountAsync(account.Id, ErasureMode.Anonymise);

        Assert.StartsWith("erased-", result.Pseudonym);
        // Account: identity gone, pseudonymised, disabled.
        Assert.Equal(result.Pseudonym, account.Name);
        Assert.Null(account.Label);
        Assert.Null(account.Email);
        Assert.False(account.Enabled);
        Assert.Empty(account.ExternalLogins);
        // Credentials and emails removed.
        Assert.Empty(ctx.ApiKeys.Store);
        Assert.Empty(ctx.Emails.Store);
        // Submission redacted; numeric kept, free-text gone.
        Assert.Equal(result.Pseudonym, submission.ServiceName);
        Assert.Empty(submission.Warnings);
        Assert.Null(submission.Samples.Single(s => s.ValueName == "str").Value);
        Assert.Equal(7L, submission.Samples.Single(s => s.ValueName == "num").Value);
        // Sample projections: string redacted, number retained.
        Assert.Null(stringSample.StringValue);
        Assert.Equal(result.Pseudonym, stringSample.ServiceName);
        Assert.Equal(42, numberSample.NumberValue);
        // Audit trail kept but pseudonymised; account row itself kept (anonymise, not delete).
        Assert.Equal(result.Pseudonym, ctx.AuditLogs.Store[0].TargetName);
        Assert.Single(ctx.Accounts.Store);
        // Anonymise does not remove notification markers.
        Assert.Single(ctx.NotificationLogs.Store);
        // The erasure itself is audited, naming only the pseudonym + mode.
        var entry = Assert.Single(ctx.Audit.Records);
        Assert.Equal(AuditChangeType.Delete, entry.Change);
        Assert.Contains("anonymise", entry.TargetName);
        Assert.Contains(result.Pseudonym, entry.TargetName);
    }

    // ── Erasure: delete ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_removes_everything_tied_to_the_subject()
    {
        var ctx = new Ctx();
        var account = ctx.SeedAccount();
        ctx.ApiKeys.Store.Add(new ApiKey { AccountId = account.Id, KeyId = "k1", Hash = "h", Salt = "s" });
        ctx.SeedSubmission(account.Id);
        ctx.SeedSample(account.Id, SchemaValueType.Number, s => s.NumberValue = 1);
        ctx.Emails.Store.Add(new EmailMessage { ToAddress = "jane@example.com", Subject = "x", TextBody = "b", RelatedAccountId = account.Id });
        ctx.AuditLogs.Store.Add(new AuditLog { TargetId = account.Id, Timestamp = Now });
        ctx.NotificationLogs.Store.Add(new NotificationLog { Key = $"missed:{account.Id}:s:v:p", CreatedAt = Now });

        await ctx.Erasure().EraseAccountAsync(account.Id, ErasureMode.Delete);

        Assert.Empty(ctx.Accounts.Store);
        Assert.Empty(ctx.ApiKeys.Store);
        Assert.Empty(ctx.Submissions.Store);
        Assert.Empty(ctx.Samples.Store);
        Assert.Empty(ctx.Emails.Store);
        Assert.Empty(ctx.AuditLogs.Store);
        Assert.Empty(ctx.NotificationLogs.Store);
        // Accountability survives: the erasure entry is recorded after the purge.
        Assert.Single(ctx.Audit.Records);
    }

    [Fact]
    public async Task Erase_unknown_account_throws_not_found()
    {
        var ctx = new Ctx();
        await Assert.ThrowsAsync<NotFoundException>(() => ctx.Erasure().EraseAccountAsync(Guid.NewGuid(), ErasureMode.Delete));
    }

    // ── Retention: cutoffs ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Retention_only_purges_data_older_than_the_configured_window()
    {
        var ctx = new Ctx();
        // Emails: old Sent (purged), recent Sent (kept), old Pending (kept — only processed mail is purged).
        ctx.Emails.Store.Add(new EmailMessage { ToAddress = "a@x", Subject = "s", TextBody = "b", Status = EmailStatus.Sent, CreatedAt = Now.AddDays(-100) });
        ctx.Emails.Store.Add(new EmailMessage { ToAddress = "a@x", Subject = "s", TextBody = "b", Status = EmailStatus.Sent, CreatedAt = Now.AddDays(-1) });
        ctx.Emails.Store.Add(new EmailMessage { ToAddress = "a@x", Subject = "s", TextBody = "b", Status = EmailStatus.Pending, CreatedAt = Now.AddDays(-100) });
        // Soft-deleted accounts: old (purged) + recent (kept).
        ctx.Accounts.Store.Add(new Account { Name = "old", IsDeleted = true, DeletedAt = Now.AddDays(-100) });
        ctx.Accounts.Store.Add(new Account { Name = "new", IsDeleted = true, DeletedAt = Now.AddDays(-2) });
        // Audit + notification markers: old (purged) + recent (kept).
        ctx.AuditLogs.Store.Add(new AuditLog { Timestamp = Now.AddDays(-100) });
        ctx.AuditLogs.Store.Add(new AuditLog { Timestamp = Now.AddDays(-2) });
        ctx.NotificationLogs.Store.Add(new NotificationLog { Key = "k1", CreatedAt = Now.AddDays(-100) });
        ctx.NotificationLogs.Store.Add(new NotificationLog { Key = "k2", CreatedAt = Now.AddDays(-2) });

        var result = await ctx.Retention(new RetentionOptions
        {
            Enabled = true, SentEmailsDays = 30, SoftDeletedDays = 30, AuditLogDays = 30, NotificationLogDays = 30,
        }).PurgeAsync();

        Assert.Equal(1, result.EmailsPurged);
        Assert.Equal(1, result.SoftDeletedPurged);
        Assert.Equal(1, result.AuditEntriesPurged);
        Assert.Equal(1, result.NotificationMarkersPurged);
        Assert.Equal(4, result.Total);
        Assert.Equal(2, ctx.Emails.Store.Count); // recent Sent + old Pending survive
        Assert.Single(ctx.Accounts.Store);
        Assert.Single(ctx.AuditLogs.Store);
        Assert.Single(ctx.NotificationLogs.Store);
    }

    [Fact]
    public async Task Retention_with_all_windows_zero_purges_nothing()
    {
        var ctx = new Ctx();
        ctx.Emails.Store.Add(new EmailMessage { ToAddress = "a@x", Subject = "s", TextBody = "b", Status = EmailStatus.Sent, CreatedAt = Now.AddDays(-9999) });
        ctx.AuditLogs.Store.Add(new AuditLog { Timestamp = Now.AddDays(-9999) });

        var result = await ctx.Retention(new RetentionOptions { Enabled = true }).PurgeAsync();

        Assert.Equal(0, result.Total);
        Assert.Single(ctx.Emails.Store);
        Assert.Single(ctx.AuditLogs.Store);
    }

    // ── DSAR: completeness ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dsar_bundle_includes_every_collection_for_the_subject_incl_outbox_emails()
    {
        var ctx = new Ctx();
        var account = ctx.SeedAccount();
        ctx.ApiKeys.Store.Add(new ApiKey { AccountId = account.Id, KeyId = "k1", Hash = "secret-hash", Salt = "secret-salt" });
        ctx.SeedSubmission(account.Id);
        ctx.SeedSample(account.Id, SchemaValueType.Number, s => s.NumberValue = 1);
        // One email by related id, one only by recipient address.
        ctx.Emails.Store.Add(new EmailMessage { ToAddress = "other@x", Subject = "rel", TextBody = "b", RelatedAccountId = account.Id });
        ctx.Emails.Store.Add(new EmailMessage { ToAddress = account.Email!, Subject = "byaddr", TextBody = "b" });
        ctx.Emails.Store.Add(new EmailMessage { ToAddress = "stranger@x", Subject = "nope", TextBody = "b" });
        ctx.AuditLogs.Store.Add(new AuditLog { TargetId = account.Id, Timestamp = Now });
        ctx.AuditLogs.Store.Add(new AuditLog { ActorId = account.Id, Timestamp = Now });
        ctx.AuditLogs.Store.Add(new AuditLog { TargetId = Guid.NewGuid(), Timestamp = Now });

        var bundle = await ctx.PersonalData().ExportForAccountAsync(account.Id);

        Assert.Equal(account.Id, bundle.Account.Id);
        var key = Assert.Single(bundle.ApiKeys);
        Assert.Equal("k1", key.KeyId); // metadata only — no Hash/Salt on the projection type
        Assert.Single(bundle.Submissions);
        Assert.Single(bundle.Samples);
        Assert.Equal(2, bundle.Emails.Count); // related + by-address, not the stranger
        Assert.Equal(2, bundle.AuditEntries.Count); // target + actor, not the unrelated entry
        Assert.Equal(Now, bundle.GeneratedAt);
    }

    [Fact]
    public async Task Dsar_unknown_account_throws_not_found()
    {
        var ctx = new Ctx();
        await Assert.ThrowsAsync<NotFoundException>(() => ctx.PersonalData().ExportForAccountAsync(Guid.NewGuid()));
    }

    // ── Test context + fakes ────────────────────────────────────────────────────────────────

    private sealed class Ctx
    {
        public readonly FakeAccounts Accounts = new();
        public readonly FakeApiKeys ApiKeys = new();
        public readonly FakeSubmissions Submissions = new();
        public readonly FakeSamples Samples = new();
        public readonly FakeEmails Emails = new();
        public readonly FakeAuditLogs AuditLogs = new();
        public readonly FakeNotificationLogs NotificationLogs = new();
        public readonly FakeSchemas Schemas = new();
        public readonly FakeReports Reports = new();
        public readonly CapturingAuditLogService Audit = new();

        public ErasureService Erasure() =>
            new(Accounts, ApiKeys, Submissions, Samples, Emails, AuditLogs, NotificationLogs, Audit);

        public RetentionService Retention(RetentionOptions options) =>
            new(Emails, Accounts, Schemas, Submissions, Samples, Reports, AuditLogs, NotificationLogs,
                new FixedTimeProvider(Now), Options.Create(options));

        public PersonalDataService PersonalData() =>
            new(Accounts, ApiKeys, Submissions, Samples, Emails, AuditLogs, new FixedTimeProvider(Now));

        public Account SeedAccount()
        {
            var a = new Account
            {
                Name = "jane", Label = "Jane", Email = "jane@example.com", Enabled = true,
                Kind = AccountKind.User, Role = AccountRole.Service,
                ExternalLogins = new() { new ExternalLogin { Provider = "Microsoft", Email = "jane@example.com" } },
            };
            Accounts.Store.Add(a);
            return a;
        }

        public Submission SeedSubmission(Guid serviceId)
        {
            var s = new Submission
            {
                ServiceAccountId = serviceId,
                ServiceName = "Jane",
                Warnings = new() { new SubmissionWarning(null, "a warning") },
                Samples = new()
                {
                    new Sample { SchemaName = "sc", ValueName = "str", Value = "Jane Doe", Timestamp = Now, Note = "secret note" },
                    new Sample { SchemaName = "sc", ValueName = "num", Value = 7L, Timestamp = Now },
                },
            };
            Submissions.Store.Add(s);
            return s;
        }

        public SampleProjection SeedSample(Guid serviceId, SchemaValueType type, Action<SampleProjection> set)
        {
            var p = new SampleProjection
            {
                SubmissionId = Guid.NewGuid(), ServiceAccountId = serviceId, ServiceName = "Jane",
                SchemaName = "sc", ValueName = type == SchemaValueType.String ? "str" : "num",
                ValueType = type, Timestamp = Now, Note = "a note",
            };
            set(p);
            Samples.Store.Add(p);
            return p;
        }
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }

    private sealed class CapturingAuditLogService : IAuditLogService
    {
        public List<AuditLog> Records { get; } = new();

        public Task RecordAsync(AuditTargetType targetType, AuditChangeType change, Guid targetId, string? targetName, CancellationToken ct = default) =>
            RecordAsync(targetType, change, targetId, targetName, null, ct);

        public Task RecordAsync(AuditTargetType targetType, AuditChangeType change, Guid targetId, string? targetName, string? note, CancellationToken ct = default)
        {
            Records.Add(new AuditLog { TargetType = targetType, Change = change, TargetId = targetId, TargetName = targetName, Note = note });
            return Task.CompletedTask;
        }

        public Task<PagedResult<AuditLog>> ListAsync(PageRequest request, AuditChangeType? change = null, AuditTargetType? targetType = null, string? nameFilter = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<PagedResult<AuditLog>> ListByTargetAsync(Guid targetId, PageRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public async IAsyncEnumerable<AuditLog> StreamAsync(AuditChangeType? change = null, AuditTargetType? targetType = null, string? nameFilter = null, DateTime? from = null, DateTime? to = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class FakeAccounts : IAccountRepository
    {
        public List<Account> Store { get; } = new();
        public Task<Account?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult(Store.FirstOrDefault(a => a.Id == id && (includeDeleted || !a.IsDeleted)));
        public Task UpdateAsync(Account account, CancellationToken ct = default) => Task.CompletedTask;
        public Task HardDeleteAsync(Guid id, CancellationToken ct = default) { Store.RemoveAll(a => a.Id == id); return Task.CompletedTask; }
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) =>
            Task.FromResult((long)Store.RemoveAll(a => a.IsDeleted && a.DeletedAt < olderThanUtc));
        public Task<Account?> GetByNameAsync(string name, bool includeDeleted = false, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Account?> GetByExternalLoginAsync(string provider, string email, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<Account>> ListAsync(PageRequest request, AccountKind? kind = null, AccountRole? role = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(Account account, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeApiKeys : IApiKeyRepository
    {
        public List<ApiKey> Store { get; } = new();
        public Task<IReadOnlyList<ApiKey>> ListByAccountAsync(Guid accountId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ApiKey>>(Store.Where(k => k.AccountId == accountId).ToList());
        public Task<long> HardDeleteByAccountAsync(Guid accountId, CancellationToken ct = default) =>
            Task.FromResult((long)Store.RemoveAll(k => k.AccountId == accountId));
        public Task<ApiKey?> GetByKeyIdAsync(string keyId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ApiKey>> GetActiveByAccountAsync(Guid accountId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(ApiKey key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(ApiKey key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Store.RemoveAll(k => k.Id == id) > 0);
    }

    private sealed class FakeSubmissions : ISubmissionRepository
    {
        public List<Submission> Store { get; } = new();
        public Task<IReadOnlyList<Submission>> ListByServiceAsync(Guid serviceId, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Submission>>(Store.Where(s => s.ServiceAccountId == serviceId && (includeDeleted || !s.IsDeleted)).ToList());
        public Task UpdateAsync(Submission submission, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> HardDeleteByServiceAsync(Guid serviceId, CancellationToken ct = default) =>
            Task.FromResult((long)Store.RemoveAll(s => s.ServiceAccountId == serviceId));
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) =>
            Task.FromResult((long)Store.RemoveAll(s => s.IsDeleted && s.DeletedAt < olderThanUtc));
        public Task<Submission?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<Submission>> ListAsync(PageRequest request, Guid? serviceId = null, DateTime? from = null, DateTime? to = null, string? schemaName = null, ApprovalStatus? approvalStatus = null, bool? draft = null, IReadOnlyCollection<Guid>? allowedServiceIds = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<long> CountBySchemaAsync(string schemaName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<long> CountByApprovalStatusAsync(ApprovalStatus status, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(Submission submission, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeSamples : ISampleRepository
    {
        public List<SampleProjection> Store { get; } = new();
        public Task<IReadOnlyList<SampleProjection>> ListByServiceAsync(Guid serviceId, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SampleProjection>>(Store.Where(s => s.ServiceAccountId == serviceId && (includeDeleted || !s.IsDeleted)).ToList());
        public Task<long> RedactByServiceAsync(Guid serviceId, string pseudonym, CancellationToken ct = default)
        {
            long n = 0;
            foreach (var s in Store.Where(s => s.ServiceAccountId == serviceId))
            {
                s.StringValue = null; s.Note = null; s.ServiceName = pseudonym; n++;
            }
            return Task.FromResult(n);
        }
        public Task<long> HardDeleteByServiceAsync(Guid serviceId, CancellationToken ct = default) =>
            Task.FromResult((long)Store.RemoveAll(s => s.ServiceAccountId == serviceId));
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) =>
            Task.FromResult((long)Store.RemoveAll(s => s.IsDeleted && s.DeletedAt < olderThanUtc));
        public Task<PagedResult<SampleProjection>> QueryAsync(SampleQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SampleProjection?> GetLatestAsync(Guid serviceId, string schemaName, string valueName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> ExistsInWindowAsync(Guid serviceId, string schemaName, string valueName, DateTime start, DateTime end, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SampleProjection>> GetAllForSchemaAsync(string schemaName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SampleProjection>> GetForExploreAsync(string schemaName, IReadOnlyList<string> valueNames, IReadOnlyList<Guid>? serviceIds, DateTime? from, DateTime? to, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ReplaceForSubmissionAsync(Guid submissionId, IEnumerable<SampleProjection> projections, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SoftDeleteForSubmissionAsync(Guid submissionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> IsSchemaInUseAsync(string schemaName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> IsAccountInUseAsync(Guid serviceAccountId, CancellationToken ct = default) => throw new NotSupportedException();
        public IQueryable<SampleProjection> AsQueryable() => throw new NotSupportedException();
    }

    private sealed class FakeEmails : IEmailQueue
    {
        public List<EmailMessage> Store { get; } = new();
        private static bool Matches(EmailMessage m, Guid accountId, string? email) =>
            m.RelatedAccountId == accountId ||
            (!string.IsNullOrWhiteSpace(email) && string.Equals(m.ToAddress, email, StringComparison.OrdinalIgnoreCase));
        public Task<IReadOnlyList<EmailMessage>> ListForAccountAsync(Guid accountId, string? email, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<EmailMessage>>(Store.Where(m => Matches(m, accountId, email)).ToList());
        public Task<long> HardDeleteForAccountAsync(Guid accountId, string? email, CancellationToken ct = default) =>
            Task.FromResult((long)Store.RemoveAll(m => Matches(m, accountId, email)));
        public Task<long> PurgeProcessedOlderThanAsync(DateTime olderThanUtc, CancellationToken ct = default) =>
            Task.FromResult((long)Store.RemoveAll(m => (m.Status == EmailStatus.Sent || m.Status == EmailStatus.Failed) && m.CreatedAt < olderThanUtc));
        public Task<Guid> EnqueueAsync(EmailRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<EmailMessage>> ListAsync(PageRequest request, EmailStatus? status = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeAuditLogs : IAuditLogRepository
    {
        public List<AuditLog> Store { get; } = new();
        public Task<IReadOnlyList<AuditLog>> ListForAccountAsync(Guid accountId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AuditLog>>(Store.Where(a => a.TargetId == accountId || a.ActorId == accountId).ToList());
        public Task<long> AnonymiseAccountAsync(Guid accountId, string pseudonym, CancellationToken ct = default)
        {
            long n = 0;
            foreach (var a in Store)
            {
                if (a.TargetId == accountId) { a.TargetName = pseudonym; n++; }
                if (a.ActorId == accountId) { a.ActorName = pseudonym; n++; }
            }
            return Task.FromResult(n);
        }
        public Task<long> HardDeleteForAccountAsync(Guid accountId, CancellationToken ct = default) =>
            Task.FromResult((long)Store.RemoveAll(a => a.TargetId == accountId || a.ActorId == accountId));
        public Task<long> PurgeOlderThanAsync(DateTime olderThanUtc, CancellationToken ct = default) =>
            Task.FromResult((long)Store.RemoveAll(a => a.Timestamp < olderThanUtc));
        public Task AddAsync(AuditLog entry, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<AuditLog>> ListAsync(PageRequest request, AuditChangeType? change = null, AuditTargetType? targetType = null, string? nameFilter = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<AuditLog>> ListByTargetAsync(Guid targetId, PageRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<AuditLog> StreamAsync(AuditChangeType? change = null, AuditTargetType? targetType = null, string? nameFilter = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeNotificationLogs : INotificationLogRepository
    {
        public List<NotificationLog> Store { get; } = new();
        public Task<long> HardDeleteForServiceAsync(Guid serviceId, CancellationToken ct = default) =>
            Task.FromResult((long)Store.RemoveAll(n => n.Key.Contains(serviceId.ToString(), StringComparison.OrdinalIgnoreCase)));
        public Task<long> PurgeOlderThanAsync(DateTime olderThanUtc, CancellationToken ct = default) =>
            Task.FromResult((long)Store.RemoveAll(n => n.CreatedAt < olderThanUtc));
    }

    private sealed class FakeSchemas : ISchemaRepository
    {
        public List<Schema> Store { get; } = new();
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) =>
            Task.FromResult((long)Store.RemoveAll(s => s.IsDeleted && s.DeletedAt < olderThanUtc));
        public Task<Schema?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Schema?> GetByNameAsync(string name, bool includeDeleted = false, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Schema>> ListVisibleToAsync(Guid serviceId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<Schema>> ListAsync(PageRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(Schema schema, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(Schema schema, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task HardDeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeReports : IReportRepository
    {
        public List<Report> Store { get; } = new();
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) =>
            Task.FromResult((long)Store.RemoveAll(r => r.IsDeleted && r.DeletedAt < olderThanUtc));
        public Task<Report?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Report?> GetByNameAsync(string name, bool includeDeleted = false, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<Report>> ListAsync(PageRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(Report report, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
