namespace Ingest.Core.Abstractions;

/// <summary>A rendered PDF document plus the file name a browser should save it as.</summary>
/// <param name="Content">The PDF bytes.</param>
/// <param name="FileName">Suggested download file name, including the <c>.pdf</c> extension.</param>
public sealed record PdfDocument(byte[] Content, string FileName);

/// <summary>
/// Produces printable PDF documents that mirror the read-only submission view. A schema export
/// lists the full field definitions (every field, regardless of gating rules, with no data);
/// a submission export lays the submitted data out in the same structure.
/// </summary>
public interface IPdfExportService
{
    /// <summary>Render a schema's full field specification (all fields, no data) as a PDF.</summary>
    /// <param name="name">Machine-style schema name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The PDF document, or <c>null</c> when no schema with that name exists.</returns>
    Task<PdfDocument?> ExportSchemaAsync(string name, CancellationToken ct = default);

    /// <summary>Render a single submission's data, laid out in its schema's structure, as a PDF.</summary>
    /// <param name="submissionId">Submission id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The PDF document, or <c>null</c> when no submission with that id exists.</returns>
    Task<PdfDocument?> ExportSubmissionAsync(Guid submissionId, CancellationToken ct = default);
}
