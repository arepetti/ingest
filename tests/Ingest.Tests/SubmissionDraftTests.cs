using System.Text.Json;
using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Approvals;
using Ingest.Infrastructure.Services;
using Ingest.Infrastructure.Validation;
using Ingest.Infrastructure.Webhooks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ingest.Tests;

/// <summary>
/// Tests for the Step 5 draft write path in <see cref="SubmissionService"/>: a draft save sets
/// <see cref="Submission.IsDraft"/>, stays out of the approval workflow and the live projection,
/// fires the draft nudge instead of the accepted webhook; publishing a draft flips it live; a
/// published submission can't be pulled back to draft; and the draft filter is threaded to the
/// repository on the list paths.
/// </summary>
public class SubmissionDraftTests
{
    private static readonly Guid ServiceId = Guid.NewGuid();

    private sealed class Harness
    {
        public FakeSubmissions Submissions { get; } = new();
        public CapturingSamples Samples { get; } = new();
        public FakeSchemas Schemas { get; } = new();
        public CountingDraftNotifier DraftNotifier { get; } = new();
        public CountingApprovalNotifier ApprovalNotifier { get; } = new();
        public CapturingWebhooks Webhooks { get; } = new();
        public SubmissionService Service { get; }

        public Harness(bool webhooksEnabled = true, bool approvalEnabled = false)
        {
            var accounts = new FakeAccounts();
            accounts.Store.Add(new Account { Id = ServiceId, Name = "roads", Label = "Roads", Email = "roads@example.com" });

            Service = new SubmissionService(
                Submissions, Samples, Schemas, new AlwaysValid(), new NCalcExpressionEvaluator(), accounts,
                TimeProvider.System, new NoopAuditLogService(), Webhooks,
                Options.Create(new WebhookOptions { Enabled = webhooksEnabled }),
                new FakeApprovalSettings(), new FakeApprovalRules(),
                Options.Create(new ApprovalOptions { Enabled = approvalEnabled }),
                ApprovalNotifier, DraftNotifier, NullLogger<SubmissionService>.Instance);
        }
    }

    private static FakeSchemas WasteSchema()
    {
        var schemas = new FakeSchemas();
        schemas.Visible.Add(new Schema
        {
            Name = "waste",
            Values =
            {
                new SchemaValue { Name = "tonnes", Type = SchemaValueType.Number, Cadence = Cadence.Yearly },
                new SchemaValue { Name = "incidents", Type = SchemaValueType.Integer, Cadence = Cadence.Yearly },
            },
        });
        return schemas;
    }

    private static SubmissionInput WasteInput() => new(new List<SampleInput>
    {
        new("waste", "tonnes", JsonNumber("12.5"), DateTime.UtcNow, null),
        new("waste", "incidents", JsonNumber("0"), DateTime.UtcNow, null),
    });

    private static JsonElement JsonNumber(string raw) => JsonDocument.Parse(raw).RootElement;

    [Fact]
    public async Task Draft_create_stays_out_of_projection_and_approval_and_fires_nudge()
    {
        var h = new Harness();
        h.Schemas.Visible.AddRange(WasteSchema().Visible);

        var result = await h.Service.CreateMineAsync(ServiceId, WasteInput(), SubmissionSource.Manual, draft: true);

        Assert.True(result.Submission.IsDraft);
        Assert.Equal(ApprovalStatus.NotRequired, result.Submission.ApprovalStatus);
        Assert.Empty(result.Submission.RequiredApprovers);
        // No live projection for a draft.
        Assert.Empty(h.Samples.LastProjections);
        // Nudge fired, accepted webhook did not.
        Assert.Equal(1, h.DraftNotifier.Count);
        Assert.DoesNotContain(WebhookEventKind.SubmissionAccepted, h.Webhooks.Kinds);
    }

    [Fact]
    public async Task Published_create_builds_projection_and_fires_accepted_webhook()
    {
        var h = new Harness();
        h.Schemas.Visible.AddRange(WasteSchema().Visible);

        var result = await h.Service.CreateMineAsync(ServiceId, WasteInput(), SubmissionSource.Manual, draft: false);

        Assert.False(result.Submission.IsDraft);
        Assert.Equal(2, h.Samples.LastProjections.Count);
        Assert.Equal(0, h.DraftNotifier.Count);
        Assert.Contains(WebhookEventKind.SubmissionAccepted, h.Webhooks.Kinds);
    }

    [Fact]
    public async Task Publishing_a_draft_flips_it_live_without_another_nudge()
    {
        var h = new Harness();
        h.Schemas.Visible.AddRange(WasteSchema().Visible);

        // Seed an existing draft owned by the caller.
        var existing = new Submission
        {
            Id = Guid.NewGuid(),
            ServiceAccountId = ServiceId,
            ServiceName = "Roads",
            IsDraft = true,
            ApprovalStatus = ApprovalStatus.NotRequired,
            Samples = new List<Sample> { new() { SchemaName = "waste", ValueName = "tonnes", Value = 1.0, Timestamp = DateTime.UtcNow } },
        };
        h.Submissions.Store.Add(existing);

        var result = await h.Service.ReplaceMineAsync(ServiceId, existing.Id, WasteInput(), SubmissionSource.Manual, draft: false);

        Assert.False(result.Submission.IsDraft);
        Assert.Equal(2, h.Samples.LastProjections.Count);
        Assert.Equal(0, h.DraftNotifier.Count);
        Assert.Contains(WebhookEventKind.SubmissionAccepted, h.Webhooks.Kinds);
    }

    [Fact]
    public async Task Cannot_return_a_published_submission_to_draft()
    {
        var h = new Harness();
        h.Schemas.Visible.AddRange(WasteSchema().Visible);

        var published = new Submission
        {
            Id = Guid.NewGuid(),
            ServiceAccountId = ServiceId,
            ServiceName = "Roads",
            IsDraft = false,
            ApprovalStatus = ApprovalStatus.NotRequired,
            Samples = new List<Sample> { new() { SchemaName = "waste", ValueName = "tonnes", Value = 1.0, Timestamp = DateTime.UtcNow } },
        };
        h.Submissions.Store.Add(published);

        await Assert.ThrowsAsync<ValidationException>(() =>
            h.Service.ReplaceMineAsync(ServiceId, published.Id, WasteInput(), SubmissionSource.Manual, draft: true));
    }

    [Fact]
    public async Task List_paths_thread_the_draft_filter_to_the_repository()
    {
        var h = new Harness();

        await h.Service.ListMineAsync(ServiceId, new PageRequest(1, 20), null, null, null, draft: true);
        Assert.Equal(true, h.Submissions.LastDraftFilter!.Value);

        await h.Service.ListAsync(new PageRequest(1, 20), null, null, null, null, approvalStatus: null, draft: false);
        Assert.Equal(false, h.Submissions.LastDraftFilter!.Value);
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

    private sealed class CountingDraftNotifier : IDraftNotificationService
    {
        public int Count { get; private set; }
        public Task NotifyDraftSavedAsync(Submission submission, CancellationToken ct = default) { Count++; return Task.CompletedTask; }
    }

    private sealed class CountingApprovalNotifier : IApprovalNotificationService
    {
        public Task NotifyPendingAsync(Submission submission, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyApprovedAsync(Submission submission, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyRejectedAsync(Submission submission, string? reason, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class CapturingWebhooks : IWebhookPublisher
    {
        public List<WebhookEventKind> Kinds { get; } = new();
        public Task<int> PublishAsync(WebhookEventKind kind, string eventId, object data, Guid? serviceAccountId, CancellationToken ct = default)
        {
            Kinds.Add(kind);
            return Task.FromResult(1);
        }
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
        public bool? LastDraftFilter { get; private set; }

        public Task<Submission?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default) =>
            Task.FromResult(Store.FirstOrDefault(s => s.Id == id && (includeDeleted || !s.IsDeleted)));
        public Task<PagedResult<Submission>> ListAsync(PageRequest request, Guid? serviceId = null, DateTime? from = null, DateTime? to = null, string? schemaName = null, ApprovalStatus? approvalStatus = null, bool? draft = null, IReadOnlyCollection<Guid>? allowedServiceIds = null, CancellationToken ct = default)
        {
            LastDraftFilter = draft;
            return Task.FromResult(new PagedResult<Submission>(Store.ToList(), Store.Count, request.Page, request.PageSize));
        }
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
