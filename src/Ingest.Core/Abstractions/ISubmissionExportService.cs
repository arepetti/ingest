using Ingest.Core.Entities;

namespace Ingest.Core.Abstractions;

/// <summary>A rendered spreadsheet plus the file name a browser should save it as.</summary>
/// <param name="Content">The XLSX bytes.</param>
/// <param name="FileName">Suggested download file name, including the <c>.xlsx</c> extension.</param>
public sealed record XlsxDocument(byte[] Content, string FileName);

/// <summary>
/// Filters for an XLSX submissions export. Mirrors the admin submissions list, but a single
/// <see cref="SchemaName"/> is required (the export lays that schema's fields out as columns).
/// </summary>
/// <param name="SchemaName">Machine-style schema name; required (one schema per export).</param>
/// <param name="ServiceId">Restrict to a single service account when set.</param>
/// <param name="From">Lower bound on submission timestamp (inclusive).</param>
/// <param name="To">Upper bound on submission timestamp (exclusive).</param>
/// <param name="ApprovalStatus">Restrict to a single approval state when set.</param>
/// <param name="Draft">Restrict to drafts (<c>true</c>) or exclude them (<c>false</c>); <c>null</c> returns both.</param>
/// <param name="IncludeDeleted">When true, soft-deleted submissions are included.</param>
/// <param name="AllowedServiceIds">Security scope: when set, only submissions from these services are exported.</param>
public sealed record SubmissionExportFilter(
    string SchemaName,
    Guid? ServiceId = null,
    DateTime? From = null,
    DateTime? To = null,
    ApprovalStatus? ApprovalStatus = null,
    bool? Draft = null,
    bool IncludeDeleted = false,
    IReadOnlyCollection<Guid>? AllowedServiceIds = null);

/// <summary>
/// Produces an XLSX workbook of submissions for a single schema — one row per submission, one column
/// per schema value (grouped by the outermost layout section), with rows grouped under per-area
/// header rows. Empty values are highlighted and a submission's warnings ride along as a cell note.
/// </summary>
public interface ISubmissionExportService
{
    /// <summary>Export the submissions matching <paramref name="filter"/> as an XLSX workbook.</summary>
    /// <param name="filter">The (single-schema) filter mirroring the submissions list.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document, or <c>null</c> when no schema with that name exists.</returns>
    Task<XlsxDocument?> ExportSubmissionsAsync(SubmissionExportFilter filter, CancellationToken ct = default);
}
