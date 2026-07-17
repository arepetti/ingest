using Ingest.Api.Auth;
using Ingest.Api.Common;
using Ingest.Api.Models;
using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ingest.Api.Controllers;

/// <summary>
/// Operator/admin-facing submission management. Reads (listing and lookup) require
/// <c>submissions:read</c>; create/replace/import require <c>submissions:submit</c>; delete requires
/// <c>submissions:delete</c>; and the pending queue + approve/reject require <c>submissions:approve</c>.
/// Admin-driven mutations stamp the audit trail (CreatedBy / ModifiedBy) with the admin's identity
/// rather than the impersonated service.
/// </summary>
[ApiController]
[Route("api/admin/submissions")]
[Authorize(Policy = Capabilities.SubmissionsRead)]
public sealed class AdminSubmissionsController(
    ISubmissionService service,
    IAuditLogService auditLog,
    IBulkImportService bulkImport,
    IPdfExportService pdfExport,
    ISubmissionExportService xlsxExport) : ControllerBase
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>List submissions across all services, optionally filtered by service and/or date range.</summary>
    /// <param name="page">1-based page number; defaults to 1.</param>
    /// <param name="pageSize">Page size; defaults to 50.</param>
    /// <param name="sort">Sort hint; <c>createdAt</c> returns newest-first.</param>
    /// <param name="includeDeleted">When true, soft-deleted submissions are included.</param>
    /// <param name="serviceId">Restrict the listing to submissions made by the given account.</param>
    /// <param name="from">Lower bound on submission timestamp (inclusive).</param>
    /// <param name="to">Upper bound on submission timestamp (exclusive).</param>
    /// <param name="schemaName">Restrict the listing to submissions for the given schema.</param>
    /// <param name="approvalStatus">Restrict the listing to a single approval state (e.g. <c>Pending</c> for the review queue).</param>
    /// <param name="draft">Restrict to drafts (<c>true</c>) or exclude them (<c>false</c>); omit to return both.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">A page of submissions.</response>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<SubmissionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sort,
        [FromQuery] bool? includeDeleted,
        [FromQuery] Guid? serviceId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? schemaName,
        [FromQuery] ApprovalStatus? approvalStatus,
        [FromQuery] bool? draft,
        CancellationToken ct)
    {
        var scope = User.CurrentAssignedServiceIds();
        // A scoped caller filtering on a single out-of-scope service sees nothing.
        if (scope.Count > 0 && serviceId is { } sid && !scope.Contains(sid))
            return Ok(new PagedResponse<SubmissionDto>(Array.Empty<SubmissionDto>(), 0, page ?? 1, pageSize ?? 50));

        var result = await service.ListAsync(
            RequestHelpers.ToPageRequest(page, pageSize, sort, includeDeleted), serviceId, from, to, schemaName, approvalStatus, draft,
            scope.Count > 0 ? scope : null, ct);
        return Ok(result.Map(SubmissionDto.From));
    }

    /// <summary>Look up any submission by id (regardless of which service owns it).</summary>
    /// <param name="id">Submission id.</param>
    /// <param name="includeDeleted">When true, returns soft-deleted submissions; otherwise they appear as 404.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The submission.</response>
    /// <response code="404">No submission with that id (or it is soft-deleted and <paramref name="includeDeleted"/> is false).</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SubmissionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] bool? includeDeleted, CancellationToken ct)
    {
        var s = await service.GetAsync(id, includeDeleted ?? false, ct);
        // Hide the existence of out-of-scope submissions behind a 404 for scoped callers.
        if (s is null || !User.CanAccessService(s.ServiceAccountId)) return NotFound();
        return Ok(SubmissionDto.From(s));
    }

    /// <summary>Page through the change history (create/edit/delete) recorded for a single submission, newest first.</summary>
    /// <param name="id">Submission id.</param>
    /// <param name="page">1-based page number; defaults to 1.</param>
    /// <param name="pageSize">Page size; defaults to 50.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">A page of audit entries for the submission.</response>
    [HttpGet("{id:guid}/history")]
    [ProducesResponseType(typeof(PagedResponse<AuditLogDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> History(
        Guid id,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        // Scoped callers may only read the history of submissions they can see.
        var s = await service.GetAsync(id, includeDeleted: true, ct);
        if (s is null || !User.CanAccessService(s.ServiceAccountId)) return NotFound();

        var result = await auditLog.ListByTargetAsync(id, RequestHelpers.ToPageRequest(page, pageSize, null, false), ct);
        return Ok(result.Map(AuditLogDto.From));
    }

    /// <summary>Export a single submission's data as a PDF.</summary>
    /// <remarks>
    /// Lays the submitted data out in its schema's structure (the same layout as the read-only
    /// submission view), one value per row with its note where present. Blank rows are shown for
    /// schema fields the submission didn't fill in.
    /// </remarks>
    /// <param name="id">Submission id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The rendered PDF.</response>
    /// <response code="404">No submission with that id (or it is outside the caller's scope).</response>
    [HttpGet("{id:guid}/export.pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportPdf(Guid id, CancellationToken ct)
    {
        // Respect the caller's service scope: out-of-scope submissions 404 just like GetById.
        var existing = await service.GetAsync(id, includeDeleted: false, ct);
        if (existing is null || !User.CanAccessService(existing.ServiceAccountId)) return NotFound();

        var doc = await pdfExport.ExportSubmissionAsync(id, ct);
        return doc is null ? NotFound() : File(doc.Content, "application/pdf", doc.FileName);
    }

    /// <summary>Export the submissions for a single schema (matching the current list filters) as an XLSX workbook.</summary>
    /// <remarks>
    /// One row per submission, one column per schema value (grouped by the outermost layout section
    /// and tinted per group), with rows banded under per-area headers. Missing values are
    /// highlighted; a submission's warnings ride along as a note on its schema-label cell. Exactly
    /// one <paramref name="schemaName"/> is required — the export is a single-schema grid.
    /// </remarks>
    /// <param name="serviceId">Restrict to a single service (ANDed with the caller's scope).</param>
    /// <param name="from">Lower bound on submission timestamp (inclusive).</param>
    /// <param name="to">Upper bound on submission timestamp (exclusive).</param>
    /// <param name="schemaName">The single schema to export; required.</param>
    /// <param name="approvalStatus">Restrict to a single approval state.</param>
    /// <param name="draft">Restrict to drafts (<c>true</c>) or exclude them (<c>false</c>); omit for both.</param>
    /// <param name="includeDeleted">When true, soft-deleted submissions are included.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The rendered XLSX.</response>
    /// <response code="400">No <paramref name="schemaName"/> was supplied.</response>
    /// <response code="404">No schema with that name exists.</response>
    [HttpGet("export.xlsx")]
    [Produces(XlsxContentType)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportXlsx(
        [FromQuery] Guid? serviceId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? schemaName,
        [FromQuery] ApprovalStatus? approvalStatus,
        [FromQuery] bool? draft,
        [FromQuery] bool? includeDeleted,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(schemaName))
            return BadRequest("A single schemaName is required for the XLSX export.");

        // Honour the caller's service scope by passing it as the allow-list; an out-of-scope
        // serviceId then simply yields an empty (header-only) workbook.
        var scope = User.CurrentAssignedServiceIds();
        var filter = new SubmissionExportFilter(
            schemaName, serviceId, from, to, approvalStatus, draft, includeDeleted ?? false,
            scope.Count > 0 ? scope : null);

        var doc = await xlsxExport.ExportSubmissionsAsync(filter, ct);
        return doc is null ? NotFound() : File(doc.Content, XlsxContentType, doc.FileName);
    }

    /// <summary>Soft-delete a submission.</summary>
    /// <remarks>
    /// The OData/PowerBI projection is rebuilt asynchronously so the deleted samples disappear
    /// from downstream queries.
    /// </remarks>
    /// <param name="id">Submission id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="204">Submission deleted (or already deleted — call is idempotent).</response>
    /// <response code="403">Caller is not an Admin.</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Capabilities.SubmissionsDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        // A scoped caller can only delete within its assigned services; out-of-scope ids 404 so the
        // existence of other services' submissions stays hidden. (DeleteAsync is otherwise idempotent.)
        var existing = await service.GetAsync(id, includeDeleted: true, ct);
        if (existing is not null && !User.CanAccessService(existing.ServiceAccountId)) return NotFound();

        await service.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Create a submission on behalf of a service account.</summary>
    /// <remarks>
    /// Convenient for back-fills, testing, and remediation. The audit trail records the admin as
    /// the creator; the submission is otherwise indistinguishable from one the service would
    /// have posted itself. The response carries the submission id and any non-blocking warnings
    /// (fired <c>Warning</c> rules or samples discarded by <c>EnabledIf</c> / <c>VisibleIf</c>).
    /// </remarks>
    /// <param name="input">Submission payload including the target <c>serviceId</c>.</param>
    /// <param name="draft">When true, save as a work-in-progress draft (relaxed validation, excluded from every live stream and from approval until published).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="201">The submission was created.</response>
    /// <response code="400">Validation failed.</response>
    /// <response code="404">No matching schema, or the target service does not exist.</response>
    /// <response code="403">Caller is not an Admin.</response>
    [HttpPost]
    [Authorize(Policy = Capabilities.SubmissionsSubmit)]
    [ProducesResponseType(typeof(SubmissionWriteResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create([FromBody] AdminSubmissionInput input, [FromQuery] bool draft, CancellationToken ct)
    {
        EnsureServiceInScope(input.ServiceAccountId);
        var written = await service.AdminCreateAsync(input, Request.ResolveSource(), draft: draft, ct: ct);
        return Created($"/api/admin/submissions/{written.Submission.Id}",
            new SubmissionWriteResponse(written.Submission.Id, written.Warnings));
    }

    /// <summary>Validate a would-be submission on behalf of a service without saving (dry run).</summary>
    /// <remarks>
    /// Backs the admin schema preview's "Validate on server" action: pick a service to validate as,
    /// supply the samples (with timestamps), and get the same verdict a real submission would produce
    /// — including cadence duplicates, history-based rules, and the would-be approval state — without
    /// writing anything. The status is always 200 (even when invalid); read <c>valid</c> / <c>errors</c>.
    /// Pass <c>?omit=cadence</c> to skip the cadence duplicate check.
    /// </remarks>
    /// <param name="input">Submission payload including the target <c>serviceAccountId</c> to validate as.</param>
    /// <param name="draft">When true, validate under the relaxed draft rules instead of a full publish.</param>
    /// <param name="omit">Comma-separated checks to skip; currently only <c>cadence</c> is supported.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Validation ran; the body carries the verdict.</response>
    /// <response code="400">The request itself was malformed (e.g. an unrecognised <c>omit</c> value).</response>
    /// <response code="404">The target service does not exist.</response>
    /// <response code="403">Caller lacks the submissions:submit capability.</response>
    [HttpPost("validate")]
    [Authorize(Policy = Capabilities.SubmissionsSubmit)]
    [ProducesResponseType(typeof(SubmissionValidationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Validate([FromBody] AdminSubmissionInput input, [FromQuery] bool draft, [FromQuery] string? omit, CancellationToken ct)
    {
        EnsureServiceInScope(input.ServiceAccountId);
        var options = RequestHelpers.ParseValidationOptions(omit);
        var outcome = await service.AdminValidateAsync(input, Request.ResolveSource(), draft, options, ct);
        return Ok(SubmissionValidationResponse.From(outcome));
    }

    /// <summary>Bulk-import historical submissions for a single service from a JSON or CSV file.</summary>
    /// <remarks>
    /// Admin-only convenience for back-filling history. Parsing is strict — a file that can't be
    /// parsed (bad JSON/CSV, missing columns, unparseable timestamps) is rejected as a whole with
    /// <c>400</c> before anything is written. Once parsing succeeds the import is <b>best-effort and
    /// not transactional</b>: each submission group is validated and persisted independently, and the
    /// returned report lists exactly which groups succeeded or failed (with their warnings/errors).
    /// Re-running a file therefore re-imports the groups that previously succeeded, so fix the
    /// reported failures and re-upload only those.
    /// </remarks>
    /// <param name="request">The target service, the file format, and the raw file content.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The file parsed; the body reports per-group success/failure.</response>
    /// <response code="400">The file could not be parsed, or contained no submissions.</response>
    /// <response code="403">Caller is not an Admin.</response>
    [HttpPost("import")]
    [Authorize(Policy = Capabilities.SubmissionsSubmit)]
    [ProducesResponseType(typeof(BulkImportResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Import([FromBody] BulkImportRequest request, CancellationToken ct)
    {
        EnsureServiceInScope(request.ServiceAccountId);
        var result = await bulkImport.ImportAsync(request.ServiceAccountId, request.Format, request.Content, ct);
        return Ok(result);
    }

    /// <summary>Replace any submission in-place, without cadence constraints.</summary>
    /// <remarks>
    /// Unlike <see cref="SubmissionsController.Replace"/>, this endpoint is not bounded by the
    /// schema's cadence window — an admin may edit historical entries arbitrarily. The audit
    /// trail records the admin as the last modifier. The response carries the submission id and
    /// any non-blocking warnings.
    /// </remarks>
    /// <param name="id">Submission id.</param>
    /// <param name="input">New submission payload.</param>
    /// <param name="draft">When true, save the (already-draft) submission as a draft; false on an existing draft publishes it. A published submission cannot be returned to draft.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The replacement succeeded.</response>
    /// <response code="400">Validation failed, or an attempt was made to return a published submission to draft.</response>
    /// <response code="404">No submission with that id (or no matching schema).</response>
    /// <response code="403">Caller is not an Admin.</response>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Capabilities.SubmissionsSubmit)]
    [ProducesResponseType(typeof(SubmissionWriteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Replace(Guid id, [FromBody] AdminSubmissionInput input, [FromQuery] bool draft, CancellationToken ct)
    {
        var existing = await service.GetAsync(id, includeDeleted: true, ct);
        if (existing is not null && !User.CanAccessService(existing.ServiceAccountId)) return NotFound();

        var written = await service.AdminReplaceAsync(id, input, Request.ResolveSource(), draft: draft, ct: ct);
        return Ok(new SubmissionWriteResponse(written.Submission.Id, written.Warnings));
    }

    /// <summary>Number of submissions currently awaiting approval. Backs the dashboard pending-approvals card.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">An object carrying the pending count.</response>
    [HttpGet("pending-count")]
    [Authorize(Policy = Capabilities.SubmissionsApprove)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> PendingCount(CancellationToken ct)
    {
        var scope = User.CurrentAssignedServiceIds();
        // Scoped callers only count the pending submissions they can actually see; the dashboard
        // card then matches their (already-scoped) review queue. Unrestricted callers use the
        // cheaper direct count.
        if (scope.Count > 0)
        {
            var page = await service.ListAsync(
                new Core.Common.PageRequest(1, 1), null, null, null, null, ApprovalStatus.Pending, draft: false, scope, ct);
            return Ok(new { count = page.Total });
        }

        var count = await service.CountPendingAsync(ct);
        return Ok(new { count });
    }

    /// <summary>Approve a pending submission.</summary>
    /// <remarks>
    /// Records the caller's approval. Once every required approver has approved (or the caller is an
    /// Admin), the submission goes live: its sample projection is built and it enters the OData feed
    /// and Explore. Requires the Approver role (or Admin); the caller must additionally be a
    /// designated approver for this submission, unless they are an Admin.
    /// </remarks>
    /// <param name="id">Submission id.</param>
    /// <param name="body">Optional note recorded against the decision.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The updated submission.</response>
    /// <response code="400">The submission is not awaiting approval.</response>
    /// <response code="403">The caller is not a designated approver (and not an Admin).</response>
    /// <response code="404">No submission with that id, or the approval workflow is disabled.</response>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = Capabilities.SubmissionsApprove)]
    [ProducesResponseType(typeof(SubmissionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApprovalDecisionRequest? body, CancellationToken ct)
    {
        var existing = await service.GetAsync(id, includeDeleted: true, ct);
        if (existing is not null && !User.CanAccessService(existing.ServiceAccountId)) return NotFound();

        var updated = await service.ApproveAsync(User.CurrentAccountId(), id, body?.Note, ct);
        return Ok(SubmissionDto.From(updated));
    }

    /// <summary>Reject a pending submission, optionally recording a reason.</summary>
    /// <remarks>
    /// The submission moves to <c>Rejected</c>: it is excluded from the OData feed and Explore but
    /// stays visible (with the reason) in the submissions list. Same authorisation as approve.
    /// </remarks>
    /// <param name="id">Submission id.</param>
    /// <param name="body">Optional reason shown to the submitter and reviewers.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The updated submission.</response>
    /// <response code="400">The submission is not awaiting approval.</response>
    /// <response code="403">The caller is not a designated approver (and not an Admin).</response>
    /// <response code="404">No submission with that id, or the approval workflow is disabled.</response>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = Capabilities.SubmissionsApprove)]
    [ProducesResponseType(typeof(SubmissionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] ApprovalDecisionRequest? body, CancellationToken ct)
    {
        var existing = await service.GetAsync(id, includeDeleted: true, ct);
        if (existing is not null && !User.CanAccessService(existing.ServiceAccountId)) return NotFound();

        var updated = await service.RejectAsync(User.CurrentAccountId(), id, body?.Note, ct);
        return Ok(SubmissionDto.From(updated));
    }

    /// <summary>
    /// Guard a service-targeting write (create/validate/import on behalf of a service) against the
    /// caller's assigned-service scope. Unrestricted callers always pass; a scoped caller naming a
    /// service outside its allowlist is refused with a 403.
    /// </summary>
    private void EnsureServiceInScope(Guid serviceId)
    {
        if (!User.CanAccessService(serviceId))
            throw new ForbiddenException("This service is outside your assigned scope.");
    }
}
