using Ingest.Core.Common;
using Ingest.Core.Entities;

namespace Ingest.Core.Abstractions;

/// <summary>Outcome of a submission write: the persisted entity plus any non-blocking warnings.</summary>
/// <param name="Submission">The persisted (or just-replaced) submission.</param>
/// <param name="Warnings">
/// Non-blocking diagnostics surfaced from the validation pass — typically firing <c>Warning</c>
/// expressions and notices about samples that were discarded by <c>EnabledIf</c> /
/// <c>VisibleIf</c>. Always non-null; empty when nothing of note happened.
/// </param>
public sealed record SubmissionWriteResult(Submission Submission, IReadOnlyList<string> Warnings);

/// <summary>
/// Outcome of a validate-only (dry-run) submission pass: the full create/replace pipeline runs —
/// mapping, the complete validator, conditional-display discards, and the would-be approval
/// stamping — but nothing is persisted and no side effect fires. Returned by the <c>validate</c>
/// endpoints so API/CI clients (and the admin preview) can see exactly what a real submission
/// would do without writing anything.
/// </summary>
/// <param name="Valid">True when no rule rejected the input — i.e. a real submission would be accepted.</param>
/// <param name="Errors">Blocking validation errors, one per rejected rule. Empty when <paramref name="Valid"/> is true.</param>
/// <param name="Warnings">Non-blocking diagnostics (fired <c>Warning</c> rules, <c>EnabledIf</c>/<c>VisibleIf</c> discard notices).</param>
/// <param name="DiscardedSamples">Samples that would be dropped before persistence because their <c>EnabledIf</c>/<c>VisibleIf</c> rule is false.</param>
/// <param name="ApprovalStatus">The approval state the submission would land in if submitted now (<c>NotRequired</c>/<c>Pending</c>).</param>
/// <param name="RequiredApprovers">The approvers that would govern the submission, when it would be held for approval; empty otherwise.</param>
public sealed record SubmissionValidationOutcome(
    bool Valid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<SampleRef> DiscardedSamples,
    ApprovalStatus ApprovalStatus,
    IReadOnlyList<ApproverSpec> RequiredApprovers);

/// <summary>
/// Submission lifecycle for both service-driven and admin-driven flows. Owns every non-validation
/// rule on submissions: caller-vs-owner matching, the Service-role cadence-window check on
/// replacement, and the projection rebuild that keeps the OData/PowerBI feed in sync. The actual
/// value/cross-value validation is delegated to <see cref="ISubmissionValidator"/>.
/// </summary>
public interface ISubmissionService
{
    // ── Service-facing (caller acts on its own data) ──

    /// <summary>Create a new submission attributed to the calling account.</summary>
    /// <param name="callerAccountId">Account id taken from the bearer credential.</param>
    /// <param name="input">Submission payload (schema name + samples).</param>
    /// <param name="source">Where the submission originated (drives the source-aware approval policy).</param>
    /// <param name="draft">When true, save as a work-in-progress draft: relaxed validation, no projection, no approval, kept out of every live stream.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted submission together with any non-blocking warnings.</returns>
    /// <exception cref="NotFoundException">The referenced schema does not exist or isn't visible to the caller.</exception>
    /// <exception cref="ValidationException">Per-value or schema-level validators rejected the payload.</exception>
    Task<SubmissionWriteResult> CreateMineAsync(Guid callerAccountId, SubmissionInput input, SubmissionSource source = SubmissionSource.Api, bool draft = false, CancellationToken ct = default);

    /// <summary>Replace one of the caller's submissions in-place.</summary>
    /// <remarks>
    /// Service accounts can only replace a submission while its cadence window is still open —
    /// e.g. a daily-cadence schema becomes immutable the next day. Admins have a parallel
    /// admin-facing method that does not enforce this rule.
    /// </remarks>
    /// <param name="callerAccountId">Account id taken from the bearer credential.</param>
    /// <param name="submissionId">Id of the submission to replace.</param>
    /// <param name="input">Replacement payload.</param>
    /// <param name="source">Where the submission originated (drives the source-aware approval policy).</param>
    /// <param name="draft">
    /// When true, save the submission as a draft (relaxed validation, no projection/approval). Only a
    /// submission that is already a draft may be saved with <paramref name="draft"/> true; once
    /// published it cannot return to draft. <paramref name="draft"/> false on an existing draft publishes it.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated submission together with any non-blocking warnings.</returns>
    /// <exception cref="NotFoundException">No such submission, or no matching schema.</exception>
    /// <exception cref="ForbiddenException">The submission belongs to a different account, or its cadence window is already closed.</exception>
    /// <exception cref="ValidationException">Per-value or schema-level validators rejected the payload, or an attempt was made to return a published submission to draft.</exception>
    Task<SubmissionWriteResult> ReplaceMineAsync(Guid callerAccountId, Guid submissionId, SubmissionInput input, SubmissionSource source = SubmissionSource.Api, bool draft = false, CancellationToken ct = default);

    /// <summary>Fetch one of the caller's own submissions by id.</summary>
    /// <param name="callerAccountId">Account id taken from the bearer credential.</param>
    /// <param name="submissionId">Submission id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The submission, or <c>null</c> if no such submission exists.</returns>
    /// <exception cref="ForbiddenException">The submission exists but belongs to a different account.</exception>
    Task<Submission?> GetMineAsync(Guid callerAccountId, Guid submissionId, CancellationToken ct = default);

    /// <summary>Page through the caller's own submissions, optionally filtered by schema and/or date range.</summary>
    /// <param name="callerAccountId">Account id taken from the bearer credential.</param>
    /// <param name="request">Paging + sort parameters.</param>
    /// <param name="from">Lower bound on submission timestamp (inclusive); <c>null</c> for no lower bound.</param>
    /// <param name="to">Upper bound on submission timestamp (exclusive); <c>null</c> for no upper bound.</param>
    /// <param name="schemaName">Restrict to submissions for this schema when non-null.</param>
    /// <param name="draft">When non-null, restrict to drafts (<c>true</c>) or non-drafts (<c>false</c>); <c>null</c> returns both.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A page of the caller's submissions.</returns>
    Task<PagedResult<Submission>> ListMineAsync(Guid callerAccountId, PageRequest request, DateTime? from, DateTime? to, string? schemaName, bool? draft = null, CancellationToken ct = default);

    /// <summary>
    /// Validate a would-be new submission for the calling account WITHOUT persisting anything. Runs
    /// the exact create pipeline (mapping, the full validator, conditional-display discards, and the
    /// would-be approval stamping) but stops before persistence — no document, projection, audit,
    /// webhook, or email. Unlike <see cref="CreateMineAsync"/> it never throws on a validation
    /// failure: the errors are returned in the outcome instead.
    /// </summary>
    /// <param name="callerAccountId">Account id taken from the bearer credential.</param>
    /// <param name="input">Submission payload to validate.</param>
    /// <param name="source">Where the submission would originate (drives the source-aware approval preview).</param>
    /// <param name="draft">When true, validate under the relaxed draft rules instead of a full publish.</param>
    /// <param name="options">Optional pipeline toggles (e.g. skip cadence); <c>null</c> runs everything.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The dry-run outcome: validity, errors, warnings, discards, and the would-be approval state.</returns>
    /// <exception cref="NotFoundException">The calling account no longer exists.</exception>
    Task<SubmissionValidationOutcome> ValidateMineAsync(Guid callerAccountId, SubmissionInput input, SubmissionSource source = SubmissionSource.Api, bool draft = false, SubmissionValidationOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Validate a would-be replacement of one of the caller's submissions WITHOUT persisting. Mirrors
    /// <see cref="ReplaceMineAsync"/> — including ownership, the draft-transition guard, the
    /// Service-role cadence-window check, and modifiability — but returns validation errors instead
    /// of throwing them, and never writes. Genuine lookup/authorization failures still throw.
    /// </summary>
    /// <param name="callerAccountId">Account id taken from the bearer credential.</param>
    /// <param name="submissionId">Id of the submission that would be replaced.</param>
    /// <param name="input">Replacement payload to validate.</param>
    /// <param name="source">Where the submission would originate (drives the source-aware approval preview).</param>
    /// <param name="draft">When true, validate under the relaxed draft rules instead of a full publish.</param>
    /// <param name="options">Optional pipeline toggles (e.g. skip cadence); <c>null</c> runs everything.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The dry-run outcome for the replacement.</returns>
    /// <exception cref="NotFoundException">No submission with that id, or no matching schema.</exception>
    /// <exception cref="ForbiddenException">The submission belongs to a different account, or its cadence window is already closed.</exception>
    /// <exception cref="ValidationException">An attempt was made to validate returning a published submission to draft.</exception>
    Task<SubmissionValidationOutcome> ValidateMineReplaceAsync(Guid callerAccountId, Guid submissionId, SubmissionInput input, SubmissionSource source = SubmissionSource.Api, bool draft = false, SubmissionValidationOptions? options = null, CancellationToken ct = default);

    // ── Admin-facing ──

    /// <summary>Page through submissions across the entire registry.</summary>
    /// <param name="request">Paging + sort parameters; <c>IncludeDeleted</c> opts soft-deleted ones in.</param>
    /// <param name="serviceId">When non-null, restrict the listing to a single service.</param>
    /// <param name="from">Lower bound on submission timestamp (inclusive).</param>
    /// <param name="to">Upper bound on submission timestamp (exclusive).</param>
    /// <param name="schemaName">When non-null, restrict the listing to submissions for a single schema.</param>
    /// <param name="approvalStatus">When non-null, restrict the listing to a single approval state.</param>
    /// <param name="draft">When non-null, restrict to drafts (<c>true</c>) or non-drafts (<c>false</c>); <c>null</c> returns both.</param>
    /// <param name="allowedServiceIds">Security scope: when non-null, only submissions owned by one of these service accounts are returned. <c>null</c> means no scope restriction.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<PagedResult<Submission>> ListAsync(PageRequest request, Guid? serviceId, DateTime? from, DateTime? to, string? schemaName, ApprovalStatus? approvalStatus = null, bool? draft = null, IReadOnlyCollection<Guid>? allowedServiceIds = null, CancellationToken ct = default);

    /// <summary>Fetch any submission by id.</summary>
    /// <param name="submissionId">Submission id.</param>
    /// <param name="includeDeleted">When true, returns soft-deleted submissions.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The submission, or <c>null</c> if no match.</returns>
    Task<Submission?> GetAsync(Guid submissionId, bool includeDeleted, CancellationToken ct = default);

    /// <summary>Create a submission on behalf of a service account; the audit trail records the calling admin.</summary>
    /// <param name="input">Submission payload including the target <c>ServiceId</c>.</param>
    /// <param name="source">Where the submission originated (drives the source-aware approval policy).</param>
    /// <param name="submittedAt">
    /// Explicit <c>SubmittedAt</c> for the persisted submission, used when back-filling historical
    /// data (e.g. bulk import) so the record is dated to when it was measured rather than to now.
    /// <c>null</c> (the default) stamps the current time as usual.
    /// </param>
    /// <param name="draft">When true, save as a work-in-progress draft: relaxed validation, no projection, no approval, kept out of every live stream.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted submission together with any non-blocking warnings.</returns>
    /// <exception cref="NotFoundException">The referenced service or schema does not exist.</exception>
    /// <exception cref="ValidationException">Validators rejected the payload.</exception>
    Task<SubmissionWriteResult> AdminCreateAsync(AdminSubmissionInput input, SubmissionSource source = SubmissionSource.Manual, DateTime? submittedAt = null, bool draft = false, CancellationToken ct = default);

    /// <summary>Replace any submission. No cadence-window restriction; the audit trail records the calling admin.</summary>
    /// <param name="submissionId">Id of the submission to replace.</param>
    /// <param name="input">Replacement payload.</param>
    /// <param name="source">Where the submission originated (drives the source-aware approval policy).</param>
    /// <param name="draft">
    /// When true, save the submission as a draft. Only a submission that is already a draft may be
    /// saved with <paramref name="draft"/> true; <paramref name="draft"/> false on an existing draft publishes it.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated submission together with any non-blocking warnings.</returns>
    /// <exception cref="NotFoundException">No submission with that id, or no matching schema.</exception>
    /// <exception cref="ValidationException">Validators rejected the payload, or an attempt was made to return a published submission to draft.</exception>
    Task<SubmissionWriteResult> AdminReplaceAsync(Guid submissionId, AdminSubmissionInput input, SubmissionSource source = SubmissionSource.Manual, bool draft = false, CancellationToken ct = default);

    /// <summary>
    /// Validate a would-be submission on behalf of a named service WITHOUT persisting. Backs the
    /// admin UI's server-side schema preview: the admin authenticates by capability (not as the
    /// service), names the service to validate as, and gets the same dry-run outcome a real
    /// submission would produce. Runs the full create pipeline minus persistence; returns errors
    /// rather than throwing them.
    /// </summary>
    /// <param name="input">Submission payload including the target <c>ServiceAccountId</c> to validate as.</param>
    /// <param name="source">Where the submission would originate (drives the source-aware approval preview).</param>
    /// <param name="draft">When true, validate under the relaxed draft rules instead of a full publish.</param>
    /// <param name="options">Optional pipeline toggles (e.g. skip cadence); <c>null</c> runs everything.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The dry-run outcome for the would-be submission.</returns>
    /// <exception cref="NotFoundException">The referenced service does not exist.</exception>
    Task<SubmissionValidationOutcome> AdminValidateAsync(AdminSubmissionInput input, SubmissionSource source = SubmissionSource.Manual, bool draft = false, SubmissionValidationOptions? options = null, CancellationToken ct = default);

    /// <summary>Soft-delete a submission and its derived sample projections. Idempotent.</summary>
    /// <param name="submissionId">Submission id.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteAsync(Guid submissionId, CancellationToken ct = default);

    // ── Approval workflow ──

    /// <summary>
    /// Record an approval decision for a pending submission. When every required approver has
    /// approved (or the caller is an Admin), the submission becomes <see cref="ApprovalStatus.Approved"/>,
    /// its sample projection is built, and it enters the live read model.
    /// </summary>
    /// <param name="approverAccountId">Account recording the decision (taken from the bearer credential).</param>
    /// <param name="submissionId">Submission to approve.</param>
    /// <param name="note">Optional note recorded against the decision.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated submission.</returns>
    /// <exception cref="NotFoundException">No such submission.</exception>
    /// <exception cref="ForbiddenException">The caller is not a designated approver (and not an Admin).</exception>
    /// <exception cref="ValidationException">The submission is not awaiting approval.</exception>
    Task<Submission> ApproveAsync(Guid approverAccountId, Guid submissionId, string? note, CancellationToken ct = default);

    /// <summary>
    /// Reject a pending submission. The submission moves to <see cref="ApprovalStatus.Rejected"/>,
    /// stays out of the live read model, but remains visible (with the reason) in the UI.
    /// </summary>
    /// <param name="approverAccountId">Account recording the decision (taken from the bearer credential).</param>
    /// <param name="submissionId">Submission to reject.</param>
    /// <param name="note">Optional reason; shown to the submitter and reviewers.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated submission.</returns>
    /// <exception cref="NotFoundException">No such submission.</exception>
    /// <exception cref="ForbiddenException">The caller is not a designated approver (and not an Admin).</exception>
    /// <exception cref="ValidationException">The submission is not awaiting approval.</exception>
    Task<Submission> RejectAsync(Guid approverAccountId, Guid submissionId, string? note, CancellationToken ct = default);

    /// <summary>Count submissions currently awaiting approval. Backs the pending-approvals dashboard card.</summary>
    /// <param name="ct">Cancellation token.</param>
    Task<long> CountPendingAsync(CancellationToken ct = default);
}
