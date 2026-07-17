using System.Text.Json;
using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Core.Validation;
using Ingest.Infrastructure.Approvals;
using Ingest.Infrastructure.Services;
using Ingest.Infrastructure.Validation;
using Ingest.Infrastructure.Webhooks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ingest.Tests;

/// <summary>
/// Tests for the submission-window gate: <see cref="SubmissionService.CreateMineAsync"/> and
/// <see cref="SubmissionService.ReplaceMineAsync"/> both reject samples/existing submissions
/// outside <c>[bucket.Start + OpenOffsetHours, bucket.End + GraceHours)</c>, drafts are exempt,
/// and admin create/replace remain fully bypassed. All-zero (default) windows reproduce the
/// pre-feature behaviour exactly, which the rest of the suite (e.g. <see cref="SubmissionDraftTests"/>)
/// already exercises implicitly by creating/replacing with "now"-timestamped samples.
/// </summary>
public class SubmissionServiceWindowTests
{
    private static readonly Guid ServiceId = Guid.NewGuid();

    private sealed class Harness
    {
        public FakeSubmissions Submissions { get; } = new();
        public CapturingSamples Samples { get; } = new();
        public FakeSchemas Schemas { get; } = new();
        public FakeAppConfigurationService AppConfig { get; } = new();
        public SubmissionService Service { get; }

        public Harness()
        {
            var accounts = new FakeAccounts();
            accounts.Store.Add(new Account { Id = ServiceId, Name = "roads", Label = "Roads", Email = "roads@example.com" });

            Service = new SubmissionService(
                Submissions, Samples, Schemas, new AlwaysValid(), new NCalcExpressionEvaluator(), accounts,
                TimeProvider.System, new NoopAuditLogService(), new NoopWebhooks(),
                Options.Create(new WebhookOptions { Enabled = false }),
                new FakeApprovalSettings(), new FakeApprovalRules(),
                Options.Create(new ApprovalOptions { Enabled = false }),
                new NoopApprovalNotifier(), new NoopDraftNotifier(), AppConfig, NullLogger<SubmissionService>.Instance);
        }
    }

    private static FakeSchemas WasteSchema()
    {
        var schemas = new FakeSchemas();
        schemas.Visible.Add(new Schema
        {
            Name = "waste",
            Values = { new SchemaValue { Name = "tonnes", Type = SchemaValueType.Number, Cadence = Cadence.Yearly } },
        });
        return schemas;
    }

    private static SubmissionInput InputFor(DateTime timestamp) => new(new List<SampleInput>
    {
        new("waste", "tonnes", JsonNumber("12.5"), timestamp, null),
    });

    private static JsonElement JsonNumber(string raw) => JsonDocument.Parse(raw).RootElement;

    // ── Create: too early ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_is_rejected_for_a_sample_in_a_future_period()
    {
        var h = new Harness();
        h.Schemas.Visible.AddRange(WasteSchema().Visible);

        // Yearly, default anchors: a timestamp next year falls in a bucket that hasn't opened yet
        // (its start is in the future), even with the default zero open offset.
        var future = new DateTime(DateTime.UtcNow.Year + 1, 6, 15, 0, 0, 0, DateTimeKind.Utc);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            h.Service.CreateMineAsync(ServiceId, InputFor(future), SubmissionSource.Manual, draft: false));
        Assert.Contains("doesn't open until", ex.Message);
    }

    [Fact]
    public async Task ValidateMineAsync_mirrors_the_create_too_early_guard()
    {
        var h = new Harness();
        h.Schemas.Visible.AddRange(WasteSchema().Visible);
        var future = new DateTime(DateTime.UtcNow.Year + 1, 6, 15, 0, 0, 0, DateTimeKind.Utc);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            h.Service.ValidateMineAsync(ServiceId, InputFor(future), SubmissionSource.Manual, draft: false));
    }

    // ── Create: already closed ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_is_rejected_for_a_sample_in_an_already_closed_period()
    {
        var h = new Harness();
        h.Schemas.Visible.AddRange(WasteSchema().Visible);

        // A timestamp from last year is in a bucket whose end (start of this year) has already
        // passed, and the default grace is zero.
        var past = new DateTime(DateTime.UtcNow.Year - 1, 6, 15, 0, 0, 0, DateTimeKind.Utc);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            h.Service.CreateMineAsync(ServiceId, InputFor(past), SubmissionSource.Manual, draft: false));
        Assert.Contains("closed on", ex.Message);
    }

    [Fact]
    public async Task Create_succeeds_for_a_closed_period_once_grace_covers_it()
    {
        var h = new Harness();
        h.Schemas.Visible.AddRange(WasteSchema().Visible);
        // Grace big enough to cover from last "start of year" to now, for any time of year.
        h.AppConfig.Windows = CadenceWindows.Default with { Yearly = new CadenceWindow(0, 24 * 400) };

        var past = new DateTime(DateTime.UtcNow.Year - 1, 6, 15, 0, 0, 0, DateTimeKind.Utc);

        var result = await h.Service.CreateMineAsync(ServiceId, InputFor(past), SubmissionSource.Manual, draft: false);
        Assert.False(result.Submission.IsDraft);
    }

    // ── Draft exemption ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Draft_create_is_exempt_from_the_window_gate()
    {
        var h = new Harness();
        h.Schemas.Visible.AddRange(WasteSchema().Visible);
        var future = new DateTime(DateTime.UtcNow.Year + 1, 6, 15, 0, 0, 0, DateTimeKind.Utc);

        var result = await h.Service.CreateMineAsync(ServiceId, InputFor(future), SubmissionSource.Manual, draft: true);
        Assert.True(result.Submission.IsDraft);
    }

    // ── Replace: grace extends the deadline ────────────────────────────────────────────────

    [Fact]
    public async Task Replace_is_rejected_once_the_grace_extended_window_has_closed()
    {
        var h = new Harness();
        h.Schemas.Visible.AddRange(WasteSchema().Visible);
        var past = new DateTime(DateTime.UtcNow.Year - 1, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var existing = new Submission
        {
            Id = Guid.NewGuid(),
            ServiceAccountId = ServiceId,
            ServiceName = "Roads",
            IsDraft = false,
            ApprovalStatus = ApprovalStatus.NotRequired,
            Samples = new List<Sample> { new() { SchemaName = "waste", ValueName = "tonnes", Value = 1.0, Timestamp = past } },
        };
        h.Submissions.Store.Add(existing);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            h.Service.ReplaceMineAsync(ServiceId, existing.Id, InputFor(past), SubmissionSource.Manual, draft: false));
        Assert.Contains("can no longer be modified", ex.Message);
    }

    [Fact]
    public async Task Replace_succeeds_once_a_configured_grace_covers_the_deadline()
    {
        var h = new Harness();
        h.Schemas.Visible.AddRange(WasteSchema().Visible);
        h.AppConfig.Windows = CadenceWindows.Default with { Yearly = new CadenceWindow(0, 24 * 400) };

        var past = new DateTime(DateTime.UtcNow.Year - 1, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var existing = new Submission
        {
            Id = Guid.NewGuid(),
            ServiceAccountId = ServiceId,
            ServiceName = "Roads",
            IsDraft = false,
            ApprovalStatus = ApprovalStatus.NotRequired,
            Samples = new List<Sample> { new() { SchemaName = "waste", ValueName = "tonnes", Value = 1.0, Timestamp = past } },
        };
        h.Submissions.Store.Add(existing);

        var result = await h.Service.ReplaceMineAsync(ServiceId, existing.Id, InputFor(past), SubmissionSource.Manual, draft: false);
        Assert.False(result.Submission.IsDraft);
    }

    [Fact]
    public async Task Draft_replace_is_exempt_from_the_grace_deadline()
    {
        var h = new Harness();
        h.Schemas.Visible.AddRange(WasteSchema().Visible);
        var past = new DateTime(DateTime.UtcNow.Year - 1, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var existing = new Submission
        {
            Id = Guid.NewGuid(),
            ServiceAccountId = ServiceId,
            ServiceName = "Roads",
            IsDraft = true,
            ApprovalStatus = ApprovalStatus.NotRequired,
            Samples = new List<Sample> { new() { SchemaName = "waste", ValueName = "tonnes", Value = 1.0, Timestamp = past } },
        };
        h.Submissions.Store.Add(existing);

        var result = await h.Service.ReplaceMineAsync(ServiceId, existing.Id, InputFor(past), SubmissionSource.Manual, draft: true);
        Assert.True(result.Submission.IsDraft);
    }

    // ── Admin bypass ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdminCreateAsync_bypasses_the_window_gate_entirely()
    {
        var h = new Harness();
        h.Schemas.Visible.AddRange(WasteSchema().Visible);
        var future = new DateTime(DateTime.UtcNow.Year + 1, 6, 15, 0, 0, 0, DateTimeKind.Utc);

        var result = await h.Service.AdminCreateAsync(
            new AdminSubmissionInput(ServiceId, InputFor(future).Samples), SubmissionSource.Manual, draft: false);
        Assert.False(result.Submission.IsDraft);
    }

    // ── Fakes ──

    private sealed class AlwaysValid : ISubmissionValidator
    {
        public Task<SubmissionValidationResult> ValidateAsync(Account service, Submission submission, bool isReplacement, Submission? existing, bool draft = false, SubmissionValidationOptions? options = null, CancellationToken ct = default) =>
            Task.FromResult(new SubmissionValidationResult(true, Array.Empty<string>(), Array.Empty<SubmissionWarning>(), new HashSet<SampleRef>()));
    }

    private sealed class FakeApprovalSettings : IApprovalSettingsService
    {
        public Task<ApprovalPolicy> GetDefaultAsync(CancellationToken ct = default) => Task.FromResult(new ApprovalPolicy());
        public Task<ApprovalPolicy> UpdateDefaultAsync(ApprovalPolicy policy, CancellationToken ct = default) => Task.FromResult(policy);
    }

    private sealed class FakeApprovalRules : IApprovalRulesService
    {
        public Task<IReadOnlyList<ApprovalRule>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ApprovalRule>>(Array.Empty<ApprovalRule>());
        public Task<ApprovalRule> CreateAsync(ApprovalRule rule, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApprovalRule> UpdateAsync(Guid id, ApprovalRule rule, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NoopDraftNotifier : IDraftNotificationService
    {
        public Task NotifyDraftSavedAsync(Submission submission, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoopApprovalNotifier : IApprovalNotificationService
    {
        public Task NotifyPendingAsync(Submission submission, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyApprovedAsync(Submission submission, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyRejectedAsync(Submission submission, string? reason, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoopWebhooks : IWebhookPublisher
    {
        public Task<int> PublishAsync(WebhookEventKind kind, string eventId, object data, Guid? serviceAccountId, CancellationToken ct = default) =>
            Task.FromResult(0);
    }

    private sealed class CapturingSamples : ISampleRepository
    {
        public List<SampleProjection> LastProjections { get; private set; } = new();
        public Task ReplaceForSubmissionAsync(Guid submissionId, IEnumerable<SampleProjection> projections, CancellationToken ct = default)
        {
            LastProjections = projections.ToList();
            return Task.CompletedTask;
        }
        public Task SoftDeleteForSubmissionAsync(Guid submissionId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<PagedResult<SampleProjection>> QueryAsync(SampleQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SampleProjection?> GetLatestAsync(Guid serviceId, string schemaName, string valueName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> ExistsInWindowAsync(Guid serviceId, string schemaName, string valueName, DateTime start, DateTime end, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SampleProjection>> GetAllForSchemaAsync(string schemaName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SampleProjection>> GetForExploreAsync(string schemaName, IReadOnlyList<string> valueNames, IReadOnlyList<Guid>? serviceIds, DateTime? from, DateTime? to, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> IsSchemaInUseAsync(string schemaName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> IsAccountInUseAsync(Guid serviceAccountId, CancellationToken ct = default) => throw new NotSupportedException();
        public IQueryable<SampleProjection> AsQueryable() => throw new NotSupportedException();
        public Task<IReadOnlyList<SampleProjection>> ListByServiceAsync(Guid serviceId, bool includeDeleted = false, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<long> RedactByServiceAsync(Guid serviceId, string pseudonym, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<long> HardDeleteByServiceAsync(Guid serviceId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeSchemas : ISchemaRepository
    {
        public List<Schema> Visible { get; } = new();
        public Task<IReadOnlyList<Schema>> ListVisibleToAsync(Guid serviceId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Schema>>(Visible);
        public Task<Schema?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Schema?> GetByNameAsync(string name, bool includeDeleted = false, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<Schema>> ListAsync(PageRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(Schema schema, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(Schema schema, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task HardDeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => throw new NotSupportedException();
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

    private sealed class FakeSubmissions : ISubmissionRepository
    {
        public List<Submission> Store { get; } = new();

        public Task<Submission?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult(Store.FirstOrDefault(s => s.Id == id && (includeDeleted || !s.IsDeleted)));
        public Task<PagedResult<Submission>> ListAsync(PageRequest request, Guid? serviceId = null, DateTime? from = null, DateTime? to = null, string? schemaName = null, ApprovalStatus? approvalStatus = null, bool? draft = null, IReadOnlyCollection<Guid>? allowedServiceIds = null, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<Submission>(Store.ToList(), Store.Count, request.Page, request.PageSize));
        public Task AddAsync(Submission submission, CancellationToken ct = default) { Store.Add(submission); return Task.CompletedTask; }
        public Task UpdateAsync(Submission submission, CancellationToken ct = default) => Task.CompletedTask;
        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> CountBySchemaAsync(string schemaName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<long> CountByApprovalStatusAsync(ApprovalStatus status, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Submission>> ListByServiceAsync(Guid serviceId, bool includeDeleted = false, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<long> HardDeleteByServiceAsync(Guid serviceId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<long> PurgeSoftDeletedAsync(DateTime olderThanUtc, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
