using Ingest.Core.Common;
using Ingest.Core.Entities;

namespace Ingest.Core.Abstractions;

/// <summary>
/// Parameters supplied by the viewer when asking the server to render a report. Every property
/// is optional; the service applies sensible defaults (current calendar month for the range, all
/// applicable submissions when <see cref="SchemaName"/> isn't pinned for a multi-target report,
/// etc.) and rejects only the combinations the report's <see cref="Report.Type"/> truly requires
/// (e.g. <see cref="SubmissionId"/> for a <see cref="ReportType.Single"/> report).
/// </summary>
/// <param name="SchemaName">
/// Schema the viewer chose to scope the report to. Required when the report targets more than
/// one schema or is global; ignored when it targets exactly one (the only candidate wins).
/// </param>
/// <param name="SubmissionId">
/// Submission to render. Required for <see cref="ReportType.Single"/>; ignored otherwise.
/// </param>
/// <param name="From">Inclusive start of the time range applied to aggregate queries / Single-pick filter. Defaults to the start of the current calendar month.</param>
/// <param name="To">Exclusive end of the time range. Defaults to "now".</param>
public sealed record ReportRenderRequest(
    string? SchemaName = null,
    Guid? SubmissionId = null,
    DateTime? From = null,
    DateTime? To = null);

/// <summary>
/// Outcome of a successful render: the produced HTML plus a small envelope describing what was
/// actually rendered (so the SPA can show the period / schema it ended up using).
/// </summary>
/// <param name="Html">Rendered HTML — already escaped, ready to drop into a sandboxed iframe via <c>srcdoc</c>.</param>
/// <param name="ReportName">Echoed report name.</param>
/// <param name="ReportLabel">Echoed report label.</param>
/// <param name="Type">Report type.</param>
/// <param name="SchemaName">Schema that was used as the data root (when applicable).</param>
/// <param name="SubmissionId">Submission that was rendered (Single only).</param>
/// <param name="From">Resolved range start (after defaulting).</param>
/// <param name="To">Resolved range end (after defaulting).</param>
public sealed record ReportRenderResult(
    string Html,
    string ReportName,
    string? ReportLabel,
    ReportType Type,
    string? SchemaName,
    Guid? SubmissionId,
    DateTime From,
    DateTime To);

/// <summary>
/// Report catalogue + render orchestration. The catalogue side is a thin wrapper around
/// <see cref="IReportRepository"/> with the extra "parse + persist atomically" semantics on
/// create; the render side picks the right data envelope based on the report's
/// <see cref="ReportType"/>, applies the caller's <see cref="ReportRenderRequest"/> filters, and
/// hands the result off to <see cref="IReportRenderer"/>.
/// </summary>
public interface IReportService
{
    /// <summary>Page through every report.</summary>
    /// <param name="request">Paging + sort parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<PagedResult<Report>> ListAsync(PageRequest request, CancellationToken ct = default);

    /// <summary>Fetch a report by id, with optional soft-deletion visibility.</summary>
    /// <param name="id">Report id.</param>
    /// <param name="includeDeleted">When true, soft-deleted reports can be returned.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The report, or <c>null</c> if no match exists.</returns>
    Task<Report?> GetByIdAsync(Guid id, bool includeDeleted, CancellationToken ct = default);

    /// <summary>Fetch a report by its unique machine-style name.</summary>
    /// <param name="name">Report name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The report, or <c>null</c> if no match exists.</returns>
    Task<Report?> GetByNameAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Upload a new report. The full document (front-matter + template) is supplied as raw text;
    /// the service parses the front matter, derives the metadata fields, and stores both the
    /// parsed entity and the original text.
    /// </summary>
    /// <param name="fileName">Original file name, used to derive a default <see cref="Report.Name"/> when the front matter doesn't specify one.</param>
    /// <param name="content">The full document (front matter + Liquid template).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted report.</returns>
    /// <exception cref="ConflictException">A report with the same name already exists.</exception>
    /// <exception cref="ValidationException">The file is unreadable, the front matter is malformed, or required metadata is missing.</exception>
    Task<Report> UploadAsync(string fileName, string content, CancellationToken ct = default);

    /// <summary>Soft-delete a report. Idempotent.</summary>
    /// <param name="id">Report id.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Render a report against the data envelope its <see cref="ReportType"/> requires.
    /// </summary>
    /// <param name="name">Report name.</param>
    /// <param name="request">Caller-supplied filters (schema / submission / date range).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rendered HTML plus the resolved render context.</returns>
    /// <exception cref="NotFoundException">No report with that name, or the referenced schema / submission does not exist.</exception>
    /// <exception cref="ValidationException">The request did not supply the parameters this report type requires.</exception>
    Task<ReportRenderResult> RenderAsync(string name, ReportRenderRequest request, CancellationToken ct = default);
}
