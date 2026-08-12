using Ingest.Api.Auth;
using Ingest.Api.Common;
using Ingest.Api.Models;
using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ingest.Api.Controllers;

/// <summary>
/// Liquid report catalogue and render endpoint. Reads (list / get / render) are available to
/// operators and admins; mutations (upload, delete) require the admin role. Service-role users
/// have no business with reports — the policies refuse them.
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize(Policy = Capabilities.ReportsRead)]
public sealed class ReportsController(IReportService service) : ControllerBase
{
    /// <summary>List every report.</summary>
    /// <param name="page">1-based page number; defaults to 1.</param>
    /// <param name="pageSize">Page size; defaults to 50.</param>
    /// <param name="sort">Sort hint; <c>createdAt</c> returns newest-first, otherwise label+name ascending.</param>
    /// <param name="includeDeleted">When true, soft-deleted reports are included.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">A page of reports.</response>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<ReportDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sort,
        [FromQuery] bool? includeDeleted,
        CancellationToken ct)
    {
        var result = await service.ListAsync(RequestHelpers.ToPageRequest(page, pageSize, sort, includeDeleted), ct);
        return Ok(result.Map(ReportDto.From));
    }

    /// <summary>Look up a single report by its machine-style name.</summary>
    /// <param name="name">Report name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The report.</response>
    /// <response code="404">No report with that name.</response>
    [HttpGet("{name}")]
    [ProducesResponseType(typeof(ReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByName(string name, CancellationToken ct)
    {
        var r = await service.GetByNameAsync(name, ct);
        return r is null
            ? NotFound(DiagnosticProblem.NotFound("Report", name))
            : Ok(ReportDto.From(r));
    }

    /// <summary>
    /// Render a report. The body is the resolved filter set: which schema to scope the report
    /// to, which submission (for Single-type reports) and the time window (for Aggregate-type).
    /// Every field is optional and the service applies sensible defaults — typically you only
    /// need to pass <c>submissionId</c> for Single reports.
    /// </summary>
    /// <param name="name">Report name.</param>
    /// <param name="req">Render parameters; all fields optional, see <see cref="RenderReportRequest"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The rendered HTML plus the resolved render context.</response>
    /// <response code="400">The render request was invalid (e.g. Single report without <c>submissionId</c>, multi-target report without <c>schemaName</c>, template parse / render failure).</response>
    /// <response code="404">No report with that name, or the referenced schema / submission does not exist.</response>
    [HttpPost("{name}/render")]
    [ProducesResponseType(typeof(ReportRenderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Render(string name, [FromBody] RenderReportRequest? req, CancellationToken ct)
    {
        var input = new ReportRenderRequest(req?.SchemaName, req?.SubmissionId, req?.From, req?.To);
        var result = await service.RenderAsync(name, input, ct);
        return Ok(new ReportRenderResponse(
            result.Html, result.ReportName, result.ReportLabel, result.Type,
            result.SchemaName, result.SubmissionId, result.From, result.To));
    }
}

/// <summary>
/// Admin-only management surface for reports: upload (with multipart and JSON variants) and
/// delete. Listing/rendering live on <see cref="ReportsController"/> so operators can use them.
/// </summary>
[ApiController]
[Route("api/admin/reports")]
[Authorize(Policy = Capabilities.ReportsManage)]
public sealed class AdminReportsController(IReportService service) : ControllerBase
{
    /// <summary>Upload a new report as a multipart file. Use this from a file picker.</summary>
    /// <remarks>
    /// Accepts a single file field named <c>file</c>. The original file name is used to derive
    /// the default report <c>name</c> when the front matter does not specify one. The full file
    /// body is stored verbatim so a future "download original" can round-trip.
    /// </remarks>
    /// <param name="file">Uploaded file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="201">The report was created.</response>
    /// <response code="400">The file is empty, the front matter is malformed, or the name could not be derived.</response>
    /// <response code="409">A report with the same name already exists.</response>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ReportDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> UploadFile(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            const string message = "No file uploaded (expected a 'file' multipart field with non-empty content).";
            return BadRequest(DiagnosticProblem.BadRequest(Diagnostic.Create(
                DiagnosticCodes.Reports.MissingUpload,
                message,
                ("field", "file"))));
        }

        using var reader = new StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync(ct);
        var created = await service.UploadAsync(file.FileName, content, ct);
        return Created($"/api/reports/{created.Name}", ReportDto.From(created));
    }

    /// <summary>Upload a new report as a JSON envelope (file name + content text).</summary>
    /// <remarks>
    /// Convenient for tooling that already has the document in memory and wants to avoid the
    /// multipart overhead. Same behaviour as the multipart variant; the body is stored verbatim.
    /// </remarks>
    /// <param name="req">Upload envelope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="201">The report was created.</response>
    /// <response code="400">Empty content, malformed front matter, or no derivable name.</response>
    /// <response code="409">A report with the same name already exists.</response>
    [HttpPost("json")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ReportDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> UploadJson([FromBody] UploadReportRequest req, CancellationToken ct)
    {
        var created = await service.UploadAsync(req.FileName, req.Content, ct);
        return Created($"/api/reports/{created.Name}", ReportDto.From(created));
    }

    /// <summary>Soft-delete a report.</summary>
    /// <remarks>
    /// The report disappears from <c>/api/reports</c> immediately. The original document text
    /// is retained in the database so an admin can still recover it via an out-of-band query if
    /// needed.
    /// </remarks>
    /// <param name="id">Report id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="204">Report deleted (or already deleted — call is idempotent).</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }
}
