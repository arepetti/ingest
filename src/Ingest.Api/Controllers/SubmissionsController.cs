using Ingest.Api.Auth;
using Ingest.Api.Common;
using Ingest.Api.Models;
using Ingest.Core.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ingest.Api.Controllers;

/// <summary>
/// Submission endpoints for the calling service account: post new samples, replace recent ones,
/// and list/look up your own data. Every action is implicitly scoped to whichever account the
/// API key resolves to — you cannot see another service's submissions through this controller.
/// </summary>
[ApiController]
[Route("api/submissions")]
[Authorize(Policy = AuthConstants.ServicePolicy)]
public sealed class SubmissionsController(ISubmissionService service) : ControllerBase
{
    /// <summary>Submit new samples for one schema.</summary>
    /// <remarks>
    /// The schema must be visible to the calling account (global or explicitly assigned). Each
    /// sample value runs through the per-value validators; once everything passes, the
    /// schema-level validators see the whole set in one go and can compare values to each other.
    /// On success a fresh id is returned — keep it if you may need to update the submission
    /// later through <see cref="Replace"/>. The response body also carries any non-blocking
    /// warnings the validator produced (e.g. fired <c>Warning</c> rules, or notices about
    /// samples that were discarded because their <c>EnabledIf</c> / <c>VisibleIf</c> rule
    /// rendered them inactive).
    /// </remarks>
    /// <param name="input">Submission payload (schema name plus the list of samples).</param>
    /// <param name="draft">When true, save as a work-in-progress draft: relaxed validation, excluded from every live stream and from approval until published.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="201">The submission was accepted; the body carries its new id and any warnings.</response>
    /// <response code="400">Validation failed. The <c>errors</c> field on the problem-details body lists every offending value/rule.</response>
    /// <response code="404">No schema with that name is visible to the caller.</response>
    [HttpPost]
    [ProducesResponseType(typeof(SubmissionWriteResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create([FromBody] SubmissionInput input, [FromQuery] bool draft, CancellationToken ct)
    {
        var written = await service.CreateMineAsync(User.CurrentAccountId(), input, Request.ResolveSource(), draft, ct);
        return Created($"/api/submissions/{written.Submission.Id}",
            new SubmissionWriteResponse(written.Submission.Id, written.Warnings)
            {
                WarningDetails = written.WarningDetails,
            });
    }

    /// <summary>Replace one of the caller's submissions in-place.</summary>
    /// <remarks>
    /// A service account can only replace a submission whose timestamp still falls inside the
    /// current cadence window for its schema — once the window has closed, the entry becomes
    /// effectively immutable for the service. Admins use the parallel admin endpoint to bypass
    /// this restriction. The response body carries the submission id and any non-blocking
    /// warnings (fired <c>Warning</c> rules, samples dropped by <c>EnabledIf</c> / <c>VisibleIf</c>).
    /// </remarks>
    /// <param name="id">Id of the submission to replace.</param>
    /// <param name="input">New submission payload.</param>
    /// <param name="draft">When true, save the (already-draft) submission as a draft; false on an existing draft publishes it. A published submission cannot be returned to draft.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The replacement succeeded; the body carries the updated submission's id and any warnings.</response>
    /// <response code="400">Validation failed, the cadence window for that submission has already closed, or an attempt was made to return a published submission to draft.</response>
    /// <response code="403">The submission belongs to a different account.</response>
    /// <response code="404">No submission with that id, or no matching schema.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(SubmissionWriteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Replace(Guid id, [FromBody] SubmissionInput input, [FromQuery] bool draft, CancellationToken ct)
    {
        var written = await service.ReplaceMineAsync(User.CurrentAccountId(), id, input, Request.ResolveSource(), draft, ct);
        return Ok(new SubmissionWriteResponse(written.Submission.Id, written.Warnings)
        {
            WarningDetails = written.WarningDetails,
        });
    }

    /// <summary>Validate a would-be submission without saving anything (dry run).</summary>
    /// <remarks>
    /// Runs the <em>exact</em> pipeline a real <see cref="Create"/> runs — schema visibility, per-value
    /// shape, value- and schema-level rules, cadence one-per-window duplicates, required values, and
    /// the would-be approval policy — but persists nothing and fires no webhook/email. Ideal for
    /// integration development and CI: post your payload and check the <c>valid</c> flag. The status
    /// is always 200 (even when invalid); read <c>valid</c> / <c>errors</c> for the verdict.
    /// Pass <c>?omit=cadence</c> to skip the context-dependent cadence duplicate check when you only
    /// want to verify the submission's shape (e.g. replaying fixtures in CI).
    /// </remarks>
    /// <param name="input">Submission payload to validate.</param>
    /// <param name="draft">When true, validate under the relaxed draft rules instead of a full publish.</param>
    /// <param name="omit">Comma-separated checks to skip; currently only <c>cadence</c> is supported.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Validation ran; the body carries the verdict (valid/errors/warnings/would-be approval).</response>
    /// <response code="400">The request itself was malformed (e.g. an unrecognised <c>omit</c> value).</response>
    [HttpPost("validate")]
    [ProducesResponseType(typeof(SubmissionValidationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Validate([FromBody] SubmissionInput input, [FromQuery] bool draft, [FromQuery] string? omit, CancellationToken ct)
    {
        var options = RequestHelpers.ParseValidationOptions(omit);
        var outcome = await service.ValidateMineAsync(User.CurrentAccountId(), input, Request.ResolveSource(), draft, options, ct);
        return Ok(SubmissionValidationResponse.From(outcome));
    }

    /// <summary>Validate a would-be replacement of one of the caller's submissions without saving (dry run).</summary>
    /// <remarks>
    /// Mirrors <see cref="Replace"/> — including the cadence-window restriction, the draft-transition
    /// rule, and per-value modifiability — but persists nothing. Returns 200 with the verdict on a
    /// validation failure; genuine problems with the request still surface as 4xx (e.g. 404 for an
    /// unknown id, 403 when the submission belongs to another account or its window has closed).
    /// </remarks>
    /// <param name="id">Id of the submission that would be replaced.</param>
    /// <param name="input">Replacement payload to validate.</param>
    /// <param name="draft">When true, validate under the relaxed draft rules instead of a full publish.</param>
    /// <param name="omit">Comma-separated checks to skip; currently only <c>cadence</c> is supported.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Validation ran; the body carries the verdict.</response>
    /// <response code="400">The request itself was malformed, or an attempt was made to return a published submission to draft.</response>
    /// <response code="403">The submission belongs to a different account, or its cadence window has already closed.</response>
    /// <response code="404">No submission with that id.</response>
    [HttpPost("{id:guid}/validate")]
    [ProducesResponseType(typeof(SubmissionValidationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ValidateReplace(Guid id, [FromBody] SubmissionInput input, [FromQuery] bool draft, [FromQuery] string? omit, CancellationToken ct)
    {
        var options = RequestHelpers.ParseValidationOptions(omit);
        var outcome = await service.ValidateMineReplaceAsync(User.CurrentAccountId(), id, input, Request.ResolveSource(), draft, options, ct);
        return Ok(SubmissionValidationResponse.From(outcome));
    }

    /// <summary>Fetch one of the caller's own submissions by id.</summary>
    /// <param name="id">Submission id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The submission.</response>
    /// <response code="404">No submission with that id, or it belongs to a different account.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SubmissionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyById(Guid id, CancellationToken ct)
    {
        var s = await service.GetMineAsync(User.CurrentAccountId(), id, ct);
        return s is null
            ? NotFound(DiagnosticProblem.NotFound("Submission", id))
            : Ok(SubmissionDto.From(s));
    }

    /// <summary>Page through the caller's own submissions, optionally filtered by date.</summary>
    /// <remarks>
    /// Use this from the admin UI's "My submissions" view or from a service's own dashboard.
    /// The caller never has to supply its own id — it is taken from the bearer credential.
    /// </remarks>
    /// <param name="page">1-based page number; defaults to 1.</param>
    /// <param name="pageSize">Page size; defaults to 50.</param>
    /// <param name="sort">Sort hint; <c>createdAt</c> returns newest-first.</param>
    /// <param name="from">Lower bound on submission timestamp (inclusive). Omit for no lower bound.</param>
    /// <param name="to">Upper bound on submission timestamp (exclusive). Omit for no upper bound.</param>
    /// <param name="schemaName">Restrict to submissions for the given schema. Omit for all schemas.</param>
    /// <param name="draft">Restrict to drafts (<c>true</c>) or exclude them (<c>false</c>); omit to return both.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">A page of submissions.</response>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<SubmissionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMine(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sort,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? schemaName,
        [FromQuery] bool? draft,
        CancellationToken ct)
    {
        var result = await service.ListMineAsync(
            User.CurrentAccountId(),
            RequestHelpers.ToPageRequest(page, pageSize, sort, false),
            from, to, schemaName, draft, ct);
        return Ok(result.Map(SubmissionDto.From));
    }
}
