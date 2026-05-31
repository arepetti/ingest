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
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted submission together with any non-blocking warnings.</returns>
    /// <exception cref="NotFoundException">The referenced schema does not exist or isn't visible to the caller.</exception>
    /// <exception cref="ValidationException">Per-value or schema-level validators rejected the payload.</exception>
    Task<SubmissionWriteResult> CreateMineAsync(Guid callerAccountId, SubmissionInput input, CancellationToken ct = default);

    /// <summary>Replace one of the caller's submissions in-place.</summary>
    /// <remarks>
    /// Service accounts can only replace a submission while its cadence window is still open —
    /// e.g. a daily-cadence schema becomes immutable the next day. Admins have a parallel
    /// admin-facing method that does not enforce this rule.
    /// </remarks>
    /// <param name="callerAccountId">Account id taken from the bearer credential.</param>
    /// <param name="submissionId">Id of the submission to replace.</param>
    /// <param name="input">Replacement payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated submission together with any non-blocking warnings.</returns>
    /// <exception cref="NotFoundException">No such submission, or no matching schema.</exception>
    /// <exception cref="ForbiddenException">The submission belongs to a different account, or its cadence window is already closed.</exception>
    /// <exception cref="ValidationException">Per-value or schema-level validators rejected the payload.</exception>
    Task<SubmissionWriteResult> ReplaceMineAsync(Guid callerAccountId, Guid submissionId, SubmissionInput input, CancellationToken ct = default);

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
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A page of the caller's submissions.</returns>
    Task<PagedResult<Submission>> ListMineAsync(Guid callerAccountId, PageRequest request, DateTime? from, DateTime? to, string? schemaName, CancellationToken ct = default);

    // ── Admin-facing ──

    /// <summary>Page through submissions across the entire registry.</summary>
    /// <param name="request">Paging + sort parameters; <c>IncludeDeleted</c> opts soft-deleted ones in.</param>
    /// <param name="serviceId">When non-null, restrict the listing to a single service.</param>
    /// <param name="from">Lower bound on submission timestamp (inclusive).</param>
    /// <param name="to">Upper bound on submission timestamp (exclusive).</param>
    /// <param name="schemaName">When non-null, restrict the listing to submissions for a single schema.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<PagedResult<Submission>> ListAsync(PageRequest request, Guid? serviceId, DateTime? from, DateTime? to, string? schemaName, CancellationToken ct = default);

    /// <summary>Fetch any submission by id.</summary>
    /// <param name="submissionId">Submission id.</param>
    /// <param name="includeDeleted">When true, returns soft-deleted submissions.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The submission, or <c>null</c> if no match.</returns>
    Task<Submission?> GetAsync(Guid submissionId, bool includeDeleted, CancellationToken ct = default);

    /// <summary>Create a submission on behalf of a service account; the audit trail records the calling admin.</summary>
    /// <param name="input">Submission payload including the target <c>ServiceId</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted submission together with any non-blocking warnings.</returns>
    /// <exception cref="NotFoundException">The referenced service or schema does not exist.</exception>
    /// <exception cref="ValidationException">Validators rejected the payload.</exception>
    Task<SubmissionWriteResult> AdminCreateAsync(AdminSubmissionInput input, CancellationToken ct = default);

    /// <summary>Replace any submission. No cadence-window restriction; the audit trail records the calling admin.</summary>
    /// <param name="submissionId">Id of the submission to replace.</param>
    /// <param name="input">Replacement payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated submission together with any non-blocking warnings.</returns>
    /// <exception cref="NotFoundException">No submission with that id, or no matching schema.</exception>
    /// <exception cref="ValidationException">Validators rejected the payload.</exception>
    Task<SubmissionWriteResult> AdminReplaceAsync(Guid submissionId, AdminSubmissionInput input, CancellationToken ct = default);

    /// <summary>Soft-delete a submission and its derived sample projections. Idempotent.</summary>
    /// <param name="submissionId">Submission id.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteAsync(Guid submissionId, CancellationToken ct = default);
}
