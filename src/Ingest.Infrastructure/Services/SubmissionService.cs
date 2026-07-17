using Ingest.Core.Abstractions;
using Ingest.Core.Approvals;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Core.Validation;
using Ingest.Infrastructure.Approvals;
using Ingest.Infrastructure.Webhooks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ingest.Infrastructure.Services;

/// <summary>
/// Default implementation of <see cref="ISubmissionService"/>. Coordinates the read/write paths
/// for service- and admin-driven submissions: schema audience lookup, sample mapping, validation
/// (through <see cref="ISubmissionValidator"/>), persistence, projection rebuild, and the
/// Service-role cadence-window check that admin endpoints intentionally bypass.
/// </summary>
public sealed class SubmissionService : ISubmissionService
{
    private readonly ISubmissionRepository _submissions;
    private readonly ISampleRepository _samples;
    private readonly ISchemaRepository _schemas;
    private readonly ISubmissionValidator _validator;
    private readonly IExpressionEvaluator _evaluator;
    private readonly IAccountRepository _accounts;
    private readonly TimeProvider _time;
    private readonly IAuditLogService _audit;
    private readonly IWebhookPublisher _webhooks;
    private readonly IApprovalSettingsService _approvalSettings;
    private readonly IApprovalRulesService _approvalRules;
    private readonly IApprovalNotificationService _approvalNotifier;
    private readonly IDraftNotificationService _draftNotifier;
    private readonly IAppConfigurationService _appConfig;
    private readonly bool _webhooksEnabled;
    private readonly bool _approvalEnabled;
    private readonly ILogger<SubmissionService> _logger;

    /// <summary>Create a new <see cref="SubmissionService"/>.</summary>
    /// <param name="submissions">Submission repository.</param>
    /// <param name="samples">Sample projection repository (rebuilt per submission save).</param>
    /// <param name="schemas">Schema repository for visibility checks.</param>
    /// <param name="validator">Validator that runs the full rule pipeline.</param>
    /// <param name="evaluator">Expression evaluator used when rebuilding calculated projection rows.</param>
    /// <param name="accounts">Account repository for owner/service lookups.</param>
    /// <param name="time">Clock used to evaluate cadence windows on replacement.</param>
    /// <param name="audit">Audit log used to record create/edit/delete changes.</param>
    /// <param name="webhooks">Publisher used to push submission events to subscribed webhook endpoints.</param>
    /// <param name="webhookOptions">Bound webhook options (only the master switch is read, to skip work when off).</param>
    /// <param name="approvalSettings">Global default approval policy provider.</param>
    /// <param name="approvalRules">Cross-cutting per-service/per-schema approval rules, applied additively to the schema/global policy.</param>
    /// <param name="approvalOptions">Bound approval options (the master switch gating the whole workflow).</param>
    /// <param name="approvalNotifier">Sends the approval-lifecycle emails (pending/approved/rejected); self-gates on the email switch.</param>
    /// <param name="draftNotifier">Sends the draft-saved nudge on every draft save; self-gates on the email switch and its rule.</param>
    /// <param name="appConfig">Application configuration provider; supplies the cadence anchors and the ingestion kill switch.</param>
    /// <param name="logger">Logger; webhook publishing failures are logged but never fail an accepted write.</param>
    public SubmissionService(
        ISubmissionRepository submissions,
        ISampleRepository samples,
        ISchemaRepository schemas,
        ISubmissionValidator validator,
        IExpressionEvaluator evaluator,
        IAccountRepository accounts,
        TimeProvider time,
        IAuditLogService audit,
        IWebhookPublisher webhooks,
        IOptions<WebhookOptions> webhookOptions,
        IApprovalSettingsService approvalSettings,
        IApprovalRulesService approvalRules,
        IOptions<ApprovalOptions> approvalOptions,
        IApprovalNotificationService approvalNotifier,
        IDraftNotificationService draftNotifier,
        IAppConfigurationService appConfig,
        ILogger<SubmissionService> logger)
    {
        _submissions = submissions;
        _samples = samples;
        _schemas = schemas;
        _validator = validator;
        _evaluator = evaluator;
        _accounts = accounts;
        _time = time;
        _audit = audit;
        _webhooks = webhooks;
        _approvalSettings = approvalSettings;
        _approvalRules = approvalRules;
        _approvalNotifier = approvalNotifier;
        _draftNotifier = draftNotifier;
        _appConfig = appConfig;
        _webhooksEnabled = webhookOptions.Value.Enabled;
        _approvalEnabled = approvalOptions.Value.Enabled;
        _logger = logger;
    }

    // ── Service-facing ──

    /// <inheritdoc />
    public async Task<SubmissionWriteResult> CreateMineAsync(Guid callerAccountId, SubmissionInput input, SubmissionSource source = SubmissionSource.Api, bool draft = false, CancellationToken ct = default)
    {
        await EnsureIngestionOpenAsync(ct);

        var account = await _accounts.GetByIdAsync(callerAccountId, ct: ct)
            ?? throw new NotFoundException("Account");
        var visible = await LoadVisibleAsync(account.Id, ct);

        var submission = MapInput(account, input, visible, source);
        submission.IsDraft = draft;

        // Submission window: a service can only create a sample once its cadence period's window
        // has opened (OpenOffsetHours) and before it has closed (GraceHours past the bucket's end).
        // Drafts are exempt — nothing is live yet, and window uniqueness is (re-)enforced by the
        // validator when they're published. Admins wanting to back-fill go through AdminCreateAsync.
        if (!draft && await WindowNotOpenError(submission.Samples, visible, ct) is { } windowError)
            throw new ForbiddenException(windowError);

        var validation = await ValidateOrThrow(account, submission, isReplacement: false, existing: null, draft, ct);

        // Strip samples the validator told us to drop (EnabledIf/VisibleIf == false). The
        // associated warnings are already in validation.Warnings; the surviving samples are
        // what gets persisted and projected. (Draft mode never discards, so this is a no-op there.)
        submission.Samples = FilterDiscarded(submission.Samples, validation.DiscardedSamples);
        submission.Warnings = validation.Warnings.ToList();

        await ApplyDraftOrApprovalAsync(submission, visible, draft, ct);
        await PersistAsync(submission, visible, isReplacement: false, ct);
        return new SubmissionWriteResult(submission, WarningMessages(validation.Warnings));
    }

    /// <inheritdoc />
    public async Task<SubmissionWriteResult> ReplaceMineAsync(Guid callerAccountId, Guid submissionId, SubmissionInput input, SubmissionSource source = SubmissionSource.Api, bool draft = false, CancellationToken ct = default)
    {
        await EnsureIngestionOpenAsync(ct);

        var existing = await _submissions.GetByIdAsync(submissionId, ct: ct)
            ?? throw new NotFoundException($"Submission '{submissionId}'");
        if (existing.ServiceAccountId != callerAccountId)
            throw new ForbiddenException("Submission belongs to a different account.");
        EnsureDraftTransitionAllowed(existing, draft);

        var account = await _accounts.GetByIdAsync(existing.ServiceAccountId, ct: ct)
            ?? throw new NotFoundException("Account");
        var visible = await LoadVisibleAsync(account.Id, ct);

        // Cadence window: a service can only revise samples whose cadence period is still open.
        // We evaluate against the EXISTING samples (each value's declared cadence + its recorded
        // timestamp) so that backdating new samples can't be used to circumvent the limit. Admins
        // wanting to override go through AdminReplaceAsync instead. Drafts are exempt — nothing is
        // live yet, and window uniqueness is (re-)enforced by the validator when they're published.
        if (!existing.IsDraft && await ClosedCadenceError(existing, visible, ct) is { } cadenceError)
            throw new ForbiddenException(cadenceError);

        var replacement = MapInput(account, input, visible, source);
        replacement.Id = existing.Id;
        var validation = await ValidateOrThrow(account, replacement, isReplacement: true, existing, draft, ct);

        existing.Samples = FilterDiscarded(replacement.Samples, validation.DiscardedSamples);
        existing.Warnings = validation.Warnings.ToList();
        existing.Source = source;
        existing.IsDraft = draft;
        await ApplyDraftOrApprovalAsync(existing, visible, draft, ct);
        await PersistAsync(existing, visible, isReplacement: true, ct);
        return new SubmissionWriteResult(existing, WarningMessages(validation.Warnings));
    }

    /// <inheritdoc />
    public async Task<Submission?> GetMineAsync(Guid callerAccountId, Guid submissionId, CancellationToken ct = default)
    {
        var s = await _submissions.GetByIdAsync(submissionId, ct: ct);
        if (s is null) return null;
        if (s.ServiceAccountId != callerAccountId)
            throw new ForbiddenException("Submission belongs to a different account.");
        return s;
    }

    /// <inheritdoc />
    public Task<PagedResult<Submission>> ListMineAsync(Guid callerAccountId, PageRequest request, DateTime? from, DateTime? to, string? schemaName, bool? draft = null, CancellationToken ct = default) =>
        _submissions.ListAsync(request, callerAccountId, from, to, schemaName, draft: draft, ct: ct);

    /// <inheritdoc />
    public async Task<SubmissionValidationOutcome> ValidateMineAsync(Guid callerAccountId, SubmissionInput input, SubmissionSource source = SubmissionSource.Api, bool draft = false, SubmissionValidationOptions? options = null, CancellationToken ct = default)
    {
        var account = await _accounts.GetByIdAsync(callerAccountId, ct: ct)
            ?? throw new NotFoundException("Account");
        var visible = await LoadVisibleAsync(account.Id, ct);

        // Mirror CreateMineAsync's window guard so the dry-run verdict matches a real create.
        if (!draft)
        {
            var mapped = MapInput(account, input, visible, source);
            if (await WindowNotOpenError(mapped.Samples, visible, ct) is { } windowError)
                throw new ForbiddenException(windowError);
        }

        return await DryRunAsync(account, input, source, draft, isReplacement: false, existing: null, options, ct, visible);
    }

    /// <inheritdoc />
    public async Task<SubmissionValidationOutcome> ValidateMineReplaceAsync(Guid callerAccountId, Guid submissionId, SubmissionInput input, SubmissionSource source = SubmissionSource.Api, bool draft = false, SubmissionValidationOptions? options = null, CancellationToken ct = default)
    {
        // Mirror ReplaceMineAsync's guards exactly so the dry-run verdict matches a real replace:
        // ownership, the draft-transition rule, and the Service-role cadence window all still apply.
        var existing = await _submissions.GetByIdAsync(submissionId, ct: ct)
            ?? throw new NotFoundException($"Submission '{submissionId}'");
        if (existing.ServiceAccountId != callerAccountId)
            throw new ForbiddenException("Submission belongs to a different account.");
        EnsureDraftTransitionAllowed(existing, draft);

        var account = await _accounts.GetByIdAsync(existing.ServiceAccountId, ct: ct)
            ?? throw new NotFoundException("Account");
        var visible = await LoadVisibleAsync(account.Id, ct);

        if (!existing.IsDraft && await ClosedCadenceError(existing, visible, ct) is { } cadenceError)
            throw new ForbiddenException(cadenceError);

        return await DryRunAsync(account, input, source, draft, isReplacement: true, existing, options, ct, visible);
    }

    // ── Admin-facing ──

    /// <inheritdoc />
    public Task<PagedResult<Submission>> ListAsync(PageRequest request, Guid? serviceId, DateTime? from, DateTime? to, string? schemaName, ApprovalStatus? approvalStatus = null, bool? draft = null, IReadOnlyCollection<Guid>? allowedServiceIds = null, CancellationToken ct = default) =>
        _submissions.ListAsync(request, serviceId, from, to, schemaName, approvalStatus, draft, allowedServiceIds, ct);

    /// <inheritdoc />
    public Task<Submission?> GetAsync(Guid submissionId, bool includeDeleted, CancellationToken ct = default) =>
        _submissions.GetByIdAsync(submissionId, includeDeleted, ct);

    /// <inheritdoc />
    public async Task<SubmissionWriteResult> AdminCreateAsync(AdminSubmissionInput input, SubmissionSource source = SubmissionSource.Manual, DateTime? submittedAt = null, bool draft = false, CancellationToken ct = default)
    {
        var service = await _accounts.GetByIdAsync(input.ServiceAccountId, ct: ct)
            ?? throw new NotFoundException($"Service '{input.ServiceAccountId}'");
        var visible = await LoadVisibleAsync(service.Id, ct);

        var submission = MapInput(service, new SubmissionInput(input.Samples), visible, source);
        submission.IsDraft = draft;
        // Back-fill case: date the record to the supplied instant (e.g. the sample timestamp on a
        // bulk import) instead of letting the repository stamp "now".
        if (submittedAt is { } at)
            submission.SubmittedAt = DateTime.SpecifyKind(at, DateTimeKind.Utc);
        var validation = await ValidateOrThrow(service, submission, isReplacement: false, existing: null, draft, ct);

        submission.Samples = FilterDiscarded(submission.Samples, validation.DiscardedSamples);
        submission.Warnings = validation.Warnings.ToList();
        await ApplyDraftOrApprovalAsync(submission, visible, draft, ct);
        await PersistAsync(submission, visible, isReplacement: false, ct);
        return new SubmissionWriteResult(submission, WarningMessages(validation.Warnings));
    }

    /// <inheritdoc />
    public async Task<SubmissionWriteResult> AdminReplaceAsync(Guid submissionId, AdminSubmissionInput input, SubmissionSource source = SubmissionSource.Manual, bool draft = false, CancellationToken ct = default)
    {
        var existing = await _submissions.GetByIdAsync(submissionId, ct: ct)
            ?? throw new NotFoundException($"Submission '{submissionId}'");
        EnsureDraftTransitionAllowed(existing, draft);

        // The service of an existing submission is immutable; the input value is ignored if it differs.
        var service = await _accounts.GetByIdAsync(existing.ServiceAccountId, ct: ct)
            ?? throw new NotFoundException($"Service '{existing.ServiceAccountId}'");
        var visible = await LoadVisibleAsync(service.Id, ct);

        var replacement = MapInput(service, new SubmissionInput(input.Samples), visible, source);
        replacement.Id = existing.Id;
        var validation = await ValidateOrThrow(service, replacement, isReplacement: true, existing, draft, ct);

        existing.Samples = FilterDiscarded(replacement.Samples, validation.DiscardedSamples);
        existing.Warnings = validation.Warnings.ToList();
        existing.Source = source;
        existing.IsDraft = draft;
        await ApplyDraftOrApprovalAsync(existing, visible, draft, ct);
        await PersistAsync(existing, visible, isReplacement: true, ct);
        return new SubmissionWriteResult(existing, WarningMessages(validation.Warnings));
    }

    /// <inheritdoc />
    public async Task<SubmissionValidationOutcome> AdminValidateAsync(AdminSubmissionInput input, SubmissionSource source = SubmissionSource.Manual, bool draft = false, SubmissionValidationOptions? options = null, CancellationToken ct = default)
    {
        var service = await _accounts.GetByIdAsync(input.ServiceAccountId, ct: ct)
            ?? throw new NotFoundException($"Service '{input.ServiceAccountId}'");
        return await DryRunAsync(service, new SubmissionInput(input.Samples), source, draft, isReplacement: false, existing: null, options, ct);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid submissionId, CancellationToken ct = default)
    {
        var existing = await _submissions.GetByIdAsync(submissionId, ct: ct);
        await _submissions.SoftDeleteAsync(submissionId, ct);
        await _samples.SoftDeleteForSubmissionAsync(submissionId, ct);
        if (existing is not null)
            await _audit.RecordAsync(AuditTargetType.Submission, AuditChangeType.Delete, existing.Id, existing.ServiceName, ct);
    }

    // ── Internals ──

    private async Task<IReadOnlyDictionary<string, Schema>> LoadVisibleAsync(Guid accountId, CancellationToken ct)
    {
        var visible = await _schemas.ListVisibleToAsync(accountId, ct);
        return visible.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static Submission MapInput(Account account, SubmissionInput input, IReadOnlyDictionary<string, Schema> visible, SubmissionSource source)
    {
        var samples = new List<Sample>(input.Samples.Count);
        foreach (var s in input.Samples)
        {
            // Look up the declared SchemaValueType so JsonValueMapper can coerce the raw
            // JsonElement into the right CLR type (Double, Long, DateTime, bool, string).
            SchemaValueType? type = null;
            if (visible.TryGetValue(s.SchemaName, out var schema))
            {
                var def = schema.Values.FirstOrDefault(v =>
                    string.Equals(v.Name, s.ValueName, StringComparison.OrdinalIgnoreCase));
                type = def?.Type;
            }
            var value = JsonValueMapper.MapValue(s.Value, type);
            samples.Add(new Sample
            {
                SchemaName = s.SchemaName,
                ValueName = s.ValueName,
                Value = value,
                Timestamp = DateTime.SpecifyKind(s.Timestamp, DateTimeKind.Utc),
                Note = s.Note,
            });
        }

        return new Submission
        {
            ServiceAccountId = account.Id,
            ServiceName = account.Name,
            Samples = samples,
            Source = source,
        };
    }

    private async Task<SubmissionValidationResult> ValidateOrThrow(Account account, Submission submission, bool isReplacement, Submission? existing, bool draft, CancellationToken ct)
    {
        var result = await _validator.ValidateAsync(account, submission, isReplacement, existing, draft, options: null, ct);
        if (!result.IsValid)
            throw new ValidationException(result.Errors);
        return result;
    }

    /// <summary>
    /// Shared validate-only (dry-run) pipeline behind every <c>validate</c> entry point. Runs exactly
    /// what a real create/replace runs up to and including the would-be approval stamping — mapping,
    /// the full validator, conditional-display discards, approval resolution — but never persists and
    /// fires no side effect. Unlike the write paths it does NOT throw on validation failure: the
    /// errors come back in the outcome so callers (CI clients, the admin preview) get the full verdict.
    /// </summary>
    private async Task<SubmissionValidationOutcome> DryRunAsync(
        Account account,
        SubmissionInput input,
        SubmissionSource source,
        bool draft,
        bool isReplacement,
        Submission? existing,
        SubmissionValidationOptions? options,
        CancellationToken ct,
        IReadOnlyDictionary<string, Schema>? visible = null)
    {
        visible ??= await LoadVisibleAsync(account.Id, ct);

        var submission = MapInput(account, input, visible, source);
        submission.IsDraft = draft;
        if (existing is not null) submission.Id = existing.Id;

        var validation = await _validator.ValidateAsync(account, submission, isReplacement, existing, draft, options, ct);

        // Mirror the write path's post-validation steps so the previewed approval state is the one a
        // real submission would land in: drop discarded samples, then resolve the approval policy.
        submission.Samples = FilterDiscarded(submission.Samples, validation.DiscardedSamples);
        submission.Warnings = validation.Warnings.ToList();
        await ApplyDraftOrApprovalAsync(submission, visible, draft, ct);

        return new SubmissionValidationOutcome(
            validation.IsValid,
            validation.Errors,
            WarningMessages(validation.Warnings),
            validation.DiscardedSamples.ToList(),
            submission.ApprovalStatus,
            submission.RequiredApprovers);
    }

    /// <summary>
    /// Reject the one illegal draft transition: a submission that is already published (live,
    /// pending, or rejected — anything that isn't currently a draft) cannot be pulled back to draft.
    /// Going the other way (draft → publish) and re-saving a draft as a draft are both fine.
    /// </summary>
    private static void EnsureDraftTransitionAllowed(Submission existing, bool draft)
    {
        if (draft && !existing.IsDraft)
            throw new ValidationException(new[]
            {
                "This submission has already been published and cannot be returned to draft. " +
                "Edit it directly, or clone it into a new draft.",
            });
    }

    /// <summary>
    /// Stamp the approval state for a write. A draft never enters the approval workflow — it is left
    /// a clean slate (no required approvers, no recorded decisions) until it is published, at which
    /// point the normal source-aware policy runs through <see cref="ApplyApprovalForWriteAsync"/>.
    /// </summary>
    private async Task ApplyDraftOrApprovalAsync(Submission submission, IReadOnlyDictionary<string, Schema> visible, bool draft, CancellationToken ct)
    {
        if (draft)
        {
            submission.ApprovalStatus = ApprovalStatus.NotRequired;
            submission.RequiredApprovers = new List<ApproverSpec>();
            submission.Approvals = new List<SubmissionApproval>();
            return;
        }
        await ApplyApprovalForWriteAsync(submission, visible, ct);
    }

    private static List<Sample> FilterDiscarded(IEnumerable<Sample> samples, IReadOnlySet<SampleRef> discarded)
    {
        if (discarded.Count == 0) return samples.ToList();
        return samples
            .Where(s => !discarded.Contains(new SampleRef(s.SchemaName, s.ValueName)))
            .ToList();
    }

    private async Task PersistAsync(Submission submission, IReadOnlyDictionary<string, Schema> visible, bool isReplacement, CancellationToken ct)
    {
        if (isReplacement)
            await _submissions.UpdateAsync(submission, ct);
        else
            await _submissions.AddAsync(submission, ct);

        // A submission only feeds the live read model (OData / Explore) once it's live: not a draft,
        // and never required approval or already approved. Drafts and Pending/Rejected submissions
        // have no projection, so replacing a previously-approved submission (which flips it to
        // Pending, or back to a draft is disallowed) also removes its live rows here via the
        // empty-list replace.
        var live = IsLive(submission);
        var projections = live
            ? SampleProjectionBuilder.Build(submission, visible, _evaluator, await _appConfig.GetCadenceAnchorsAsync(ct))
            : Enumerable.Empty<SampleProjection>();
        await _samples.ReplaceForSubmissionAsync(submission.Id, projections, ct);

        await _audit.RecordAsync(
            AuditTargetType.Submission,
            isReplacement ? AuditChangeType.Edit : AuditChangeType.Create,
            submission.Id,
            submission.ServiceName,
            ct: ct);

        // The accepted webhook fires only when the submission is actually live. For approval-gated
        // submissions that happens later, on the approve transition (see ApproveAsync). A submission
        // held Pending instead emits the pending-approval signal so reviewers are alerted. A draft
        // emits no webhook at all — it isn't a business event yet — but does send a deliberate nudge
        // to the people filling it in (Step 6), on every save (create and re-save).
        if (submission.IsDraft)
        {
            await _draftNotifier.NotifyDraftSavedAsync(submission, ct);
        }
        else if (live)
        {
            await PublishAcceptedAsync(submission, isReplacement, ct);
        }
        else if (submission.ApprovalStatus == ApprovalStatus.Pending)
        {
            var writtenAt = (isReplacement ? submission.ReplacedAt : submission.SubmittedAt) ?? submission.SubmittedAt;
            await PublishApprovalEventAsync(WebhookEventKind.SubmissionPendingApproval, "pending", writtenAt, submission, null, ct);
            await _approvalNotifier.NotifyPendingAsync(submission, ct);
        }
    }

    private static bool IsLive(Submission submission) =>
        !submission.IsDraft &&
        submission.ApprovalStatus is ApprovalStatus.NotRequired or ApprovalStatus.Approved;

    /// <summary>
    /// Resolve the effective approval policy for a write and stamp the submission accordingly. Always
    /// clears any prior recorded decisions (a re-send resets approval). When approval is required for
    /// the submission's schema(s) and source, the submission is held <see cref="ApprovalStatus.Pending"/>
    /// with a snapshot of the governing approvers; otherwise it is <see cref="ApprovalStatus.NotRequired"/>.
    /// </summary>
    private async Task ApplyApprovalForWriteAsync(Submission submission, IReadOnlyDictionary<string, Schema> visible, CancellationToken ct)
    {
        // A write always starts a fresh approval cycle.
        submission.Approvals = new List<SubmissionApproval>();

        if (!_approvalEnabled)
        {
            submission.ApprovalStatus = ApprovalStatus.NotRequired;
            submission.RequiredApprovers = new List<ApproverSpec>();
            return;
        }

        var globalDefault = await _approvalSettings.GetDefaultAsync(ct);
        var rules = await _approvalRules.ListAsync(ct);
        var schemaNames = submission.Samples
            .Select(s => s.SchemaName)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        // Merge approver snapshots across every schema the submission touches (normally just one).
        // Required beats Optional when the same account appears in more than one policy.
        var merged = new Dictionary<Guid, ApproverSpec>();
        var required = false;

        // Local merge so the schema policy and every matching cross-cutting rule funnel into the
        // same snapshot. Returns true when the resolved policy actually gates this submission.
        bool MergeResolved(ResolvedApproval resolved)
        {
            if (!resolved.Required) return false;
            foreach (var spec in resolved.Approvers)
            {
                // Bind the dynamic "service owner" approver to the account that actually sent this
                // submission, so downstream decision/completeness logic (which keys on AccountId)
                // treats it like any other designated approver.
                var accountId = spec.Kind == ApproverKind.ServiceOwner ? submission.ServiceAccountId : spec.AccountId;
                if (merged.TryGetValue(accountId, out var existing))
                {
                    if (existing.Requirement == ApproverRequirement.Optional && spec.Requirement == ApproverRequirement.Required)
                        existing.Requirement = ApproverRequirement.Required;
                }
                else
                {
                    merged[accountId] = new ApproverSpec { AccountId = accountId, Kind = spec.Kind, Requirement = spec.Requirement };
                }
            }
            return true;
        }

        foreach (var name in schemaNames)
        {
            visible.TryGetValue(name, out var schema);

            // 1) The schema's own policy (deferring to the global default when set to UseGlobalDefault).
            if (MergeResolved(ApprovalPolicyResolver.Resolve(true, schema?.Approval, globalDefault, submission.Source)))
                required = true;

            // 2) Cross-cutting rules that target this (service, schema). Additive — they can gate a
            //    submission even when the schema/global policy doesn't, and merge their approvers in.
            foreach (var rule in rules)
            {
                if (!ApprovalRuleMatcher.Matches(rule, submission.ServiceAccountId, schema?.Id)) continue;
                if (MergeResolved(ApprovalPolicyResolver.Resolve(true, rule.Policy, globalDefault, submission.Source)))
                    required = true;
            }
        }

        if (required)
        {
            submission.ApprovalStatus = ApprovalStatus.Pending;
            submission.RequiredApprovers = merged.Values.ToList();
        }
        else
        {
            submission.ApprovalStatus = ApprovalStatus.NotRequired;
            submission.RequiredApprovers = new List<ApproverSpec>();
        }
    }

    /// <inheritdoc />
    public async Task<Submission> ApproveAsync(Guid approverAccountId, Guid submissionId, string? note, CancellationToken ct = default)
    {
        var (submission, approver) = await LoadForDecisionAsync(approverAccountId, submissionId, ct);

        // Record (or overwrite) this approver's decision for the current cycle.
        RecordDecision(submission, approver, ApprovalDecision.Approved, note);

        var isAdmin = approver.Role == AccountRole.Admin;
        var complete = isAdmin || ApprovalPolicyResolver.IsComplete(submission.RequiredApprovers, submission.Approvals);

        if (complete)
        {
            submission.ApprovalStatus = ApprovalStatus.Approved;
            var visible = await LoadVisibleAsync(submission.ServiceAccountId, ct);
            await _submissions.UpdateAsync(submission, ct);
            var projections = SampleProjectionBuilder.Build(submission, visible, _evaluator, await _appConfig.GetCadenceAnchorsAsync(ct));
            await _samples.ReplaceForSubmissionAsync(submission.Id, projections, ct);
            await _audit.RecordAsync(AuditTargetType.Submission, AuditChangeType.Approve, submission.Id, submission.ServiceName, note, ct);
            await PublishAcceptedAsync(submission, isReplacement: submission.ReplacedAt is not null, ct);
            await PublishApprovalEventAsync(WebhookEventKind.SubmissionApproved, "approved", _time.GetUtcNow().UtcDateTime, submission, note, ct);
            await _approvalNotifier.NotifyApprovedAsync(submission, ct);
        }
        else
        {
            await _submissions.UpdateAsync(submission, ct);
            await _audit.RecordAsync(AuditTargetType.Submission, AuditChangeType.Approve, submission.Id, submission.ServiceName, note, ct);
        }

        return submission;
    }

    /// <inheritdoc />
    public async Task<Submission> RejectAsync(Guid approverAccountId, Guid submissionId, string? note, CancellationToken ct = default)
    {
        var (submission, approver) = await LoadForDecisionAsync(approverAccountId, submissionId, ct);

        RecordDecision(submission, approver, ApprovalDecision.Rejected, note);
        submission.ApprovalStatus = ApprovalStatus.Rejected;
        await _submissions.UpdateAsync(submission, ct);

        // A rejected submission must not appear in the live read model. It never had a projection
        // while Pending, but remove defensively in case it was approved-then-rejected by an admin.
        await _samples.ReplaceForSubmissionAsync(submission.Id, Enumerable.Empty<SampleProjection>(), ct);
        await _audit.RecordAsync(AuditTargetType.Submission, AuditChangeType.Reject, submission.Id, submission.ServiceName, note, ct);
        await PublishApprovalEventAsync(WebhookEventKind.SubmissionRejected, "rejected", _time.GetUtcNow().UtcDateTime, submission, note, ct);
        await _approvalNotifier.NotifyRejectedAsync(submission, note, ct);
        return submission;
    }

    /// <inheritdoc />
    public Task<long> CountPendingAsync(CancellationToken ct = default) =>
        _approvalEnabled ? _submissions.CountByApprovalStatusAsync(ApprovalStatus.Pending, ct) : Task.FromResult(0L);

    private async Task<(Submission submission, Account approver)> LoadForDecisionAsync(Guid approverAccountId, Guid submissionId, CancellationToken ct)
    {
        if (!_approvalEnabled)
            throw new NotFoundException("Approval workflow is disabled.");

        var submission = await _submissions.GetByIdAsync(submissionId, ct: ct)
            ?? throw new NotFoundException($"Submission '{submissionId}'");
        if (submission.ApprovalStatus != ApprovalStatus.Pending)
            throw new ValidationException(new[] { "Submission is not awaiting approval." });

        var approver = await _accounts.GetByIdAsync(approverAccountId, ct: ct)
            ?? throw new NotFoundException("Account");

        // Admins may always decide; otherwise the caller must be a designated approver for this submission.
        var designated = submission.RequiredApprovers.Any(a => a.AccountId == approver.Id);
        if (approver.Role != AccountRole.Admin && !designated)
            throw new ForbiddenException("You are not a designated approver for this submission.");

        return (submission, approver);
    }

    /// <summary>
    /// Flatten structured warnings to the plain message strings the write/validate responses expose.
    /// The associated value name is persisted on the submission (and used by the XLSX export) but is
    /// not part of the API contract, so callers keep seeing the same list of strings as before.
    /// </summary>
    private static IReadOnlyList<string> WarningMessages(IReadOnlyList<SubmissionWarning> warnings) =>
        warnings.Select(w => w.Message).ToList();

    private void RecordDecision(Submission submission, Account approver, ApprovalDecision decision, string? note)
    {
        submission.Approvals.RemoveAll(a => a.ApproverAccountId == approver.Id);
        submission.Approvals.Add(new SubmissionApproval
        {
            ApproverAccountId = approver.Id,
            ApproverName = approver.Name,
            Decision = decision,
            DecidedAt = _time.GetUtcNow().UtcDateTime,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        });
    }

    /// <summary>
    /// Push <c>submission.accepted</c> (and <c>submission.warnings</c> when warnings are present) to
    /// any subscribed webhook endpoints. Best-effort and isolated: the submission is already
    /// persisted, so a webhook hiccup must never turn an accepted write into an error.
    /// </summary>
    private async Task PublishAcceptedAsync(Submission submission, bool isReplacement, CancellationToken ct)
    {
        if (!_webhooksEnabled) return;

        try
        {
            // The repository stamps SubmittedAt/ReplacedAt on the entity during PersistAsync, so a
            // deterministic per-write id is available here for dedupe + idempotency.
            var writtenAt = (isReplacement ? submission.ReplacedAt : submission.SubmittedAt) ?? submission.SubmittedAt;
            var data = new
            {
                submissionId = submission.Id,
                serviceAccountId = submission.ServiceAccountId,
                serviceName = submission.ServiceName,
                isReplacement,
                submittedAt = submission.SubmittedAt,
                replacedAt = submission.ReplacedAt,
                sampleCount = submission.Samples.Count,
                schemas = submission.Samples.Select(s => s.SchemaName).Distinct().ToList(),
                // Keep the historical string-array shape for webhook consumers; the value name is
                // an internal detail carried on the entity, not part of the public payload.
                warnings = submission.Warnings.Select(w => w.Message).ToList(),
            };

            await _webhooks.PublishAsync(
                WebhookEventKind.SubmissionAccepted,
                $"accepted:{submission.Id}:{writtenAt:o}",
                data, submission.ServiceAccountId, ct);

            if (submission.Warnings.Count > 0)
                await _webhooks.PublishAsync(
                    WebhookEventKind.SubmissionWarnings,
                    $"warnings:{submission.Id}:{writtenAt:o}",
                    data, submission.ServiceAccountId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Webhook publish failed for submission {Id}; the submission was still accepted.", submission.Id);
        }
    }

    /// <summary>
    /// Push one of the approval-lifecycle webhooks (<c>submission.pending_approval</c> /
    /// <c>submission.approved</c> / <c>submission.rejected</c>) to any subscribed endpoints.
    /// Best-effort and isolated: the decision is already persisted, so a webhook hiccup must never
    /// turn a recorded approval/rejection into an error.
    /// </summary>
    private async Task PublishApprovalEventAsync(WebhookEventKind kind, string idPrefix, DateTime stamp, Submission submission, string? note, CancellationToken ct)
    {
        if (!_webhooksEnabled) return;

        try
        {
            var data = new
            {
                submissionId = submission.Id,
                serviceAccountId = submission.ServiceAccountId,
                serviceName = submission.ServiceName,
                approvalStatus = submission.ApprovalStatus.ToString(),
                submittedAt = submission.SubmittedAt,
                replacedAt = submission.ReplacedAt,
                sampleCount = submission.Samples.Count,
                schemas = submission.Samples.Select(s => s.SchemaName).Distinct().ToList(),
                note,
            };

            await _webhooks.PublishAsync(kind, $"{idPrefix}:{submission.Id}:{stamp:o}", data, submission.ServiceAccountId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Webhook publish failed for {Kind} on submission {Id}.", kind.ToWire(), submission.Id);
        }
    }

    /// <summary>
    /// Returns null when every sample in the submission still falls inside its (per-value)
    /// submission window (the cadence bucket extended by <see cref="CadenceWindow.GraceHours"/>);
    /// otherwise returns a human-readable message naming the first closed (schema, value) pair.
    /// Samples whose schema/value can no longer be resolved are ignored — they're already in a
    /// broken state that the validator will flag separately.
    /// </summary>
    private async Task<string?> ClosedCadenceError(Submission existing, IReadOnlyDictionary<string, Schema> visible, CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var anchors = await _appConfig.GetCadenceAnchorsAsync(ct);
        var windows = await _appConfig.GetCadenceWindowsAsync(ct);
        foreach (var sample in existing.Samples)
        {
            if (!visible.TryGetValue(sample.SchemaName, out var schema)) continue;
            var def = schema.Values.FirstOrDefault(v =>
                string.Equals(v.Name, sample.ValueName, StringComparison.OrdinalIgnoreCase));
            if (def is null) continue;

            var (_, windowEnd) = CadenceCalculator.WindowFor(def.Cadence, sample.Timestamp, anchors, windows);
            if (windowEnd <= now)
            {
                return $"Submission can no longer be modified: the {def.Cadence.ToString().ToLowerInvariant()} " +
                       $"period for '{sample.SchemaName}.{sample.ValueName}' closed on {windowEnd:u}. " +
                       $"Ask an administrator to amend it on your behalf.";
            }
        }
        return null;
    }

    /// <summary>
    /// Returns null when every incoming sample's timestamp falls inside its (per-value) submission
    /// window; otherwise a human-readable message naming the first sample that's either not open
    /// yet (before <see cref="CadenceWindow.OpenOffsetHours"/> has elapsed past the bucket's start)
    /// or already closed (past <see cref="CadenceWindow.GraceHours"/> after the bucket's end). Used
    /// on the create path only — <see cref="ClosedCadenceError"/> is its replace-path counterpart,
    /// which only checks the "already closed" side since a submission being replaced necessarily
    /// already exists. Samples whose schema/value can't be resolved are ignored — the validator
    /// flags those separately.
    /// </summary>
    private async Task<string?> WindowNotOpenError(IEnumerable<Sample> samples, IReadOnlyDictionary<string, Schema> visible, CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var anchors = await _appConfig.GetCadenceAnchorsAsync(ct);
        var windows = await _appConfig.GetCadenceWindowsAsync(ct);
        foreach (var sample in samples)
        {
            if (!visible.TryGetValue(sample.SchemaName, out var schema)) continue;
            var def = schema.Values.FirstOrDefault(v =>
                string.Equals(v.Name, sample.ValueName, StringComparison.OrdinalIgnoreCase));
            if (def is null) continue;

            var (windowStart, windowEnd) = CadenceCalculator.WindowFor(def.Cadence, sample.Timestamp, anchors, windows);
            if (now < windowStart)
            {
                return $"Submission cannot be created yet: the {def.Cadence.ToString().ToLowerInvariant()} " +
                       $"period for '{sample.SchemaName}.{sample.ValueName}' doesn't open until {windowStart:u}.";
            }
            if (now >= windowEnd)
            {
                return $"Submission can no longer be created: the {def.Cadence.ToString().ToLowerInvariant()} " +
                       $"period for '{sample.SchemaName}.{sample.ValueName}' closed on {windowEnd:u}. " +
                       $"Ask an administrator to amend it on your behalf.";
            }
        }
        return null;
    }

    /// <summary>
    /// Guard for the three service-facing write paths (<see cref="CreateMineAsync"/>,
    /// <see cref="ReplaceMineAsync"/>, and — via <c>BulkImportService</c>/the Teams inbound
    /// endpoint — bulk import and Teams submissions). Throws <see cref="ServiceUnavailableException"/>
    /// when the global kill switch is on. Admin-facing writes (<see cref="AdminCreateAsync"/>,
    /// <see cref="AdminReplaceAsync"/>) intentionally never call this, so operators can still
    /// remediate while ingestion is frozen. Validate-only (dry-run) callers also skip it.
    /// </summary>
    private async Task EnsureIngestionOpenAsync(CancellationToken ct)
    {
        var status = await _appConfig.GetIngestionStatusAsync(ct);
        if (status.Closed)
            throw new ServiceUnavailableException(
                string.IsNullOrWhiteSpace(status.Message)
                    ? "Submissions are temporarily closed."
                    : status.Message!);
    }
}
