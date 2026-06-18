namespace Ingest.Core.Abstractions;

/// <summary>Supported on-the-wire formats for an admin bulk import file.</summary>
public enum BulkImportFormat
{
    /// <summary>A JSON document: either an array of submissions or an object with a <c>submissions</c> array.</summary>
    Json = 0,

    /// <summary>A CSV document, one sample per row, grouped into submissions by an optional <c>group</c> column.</summary>
    Csv = 1,
}

/// <summary>Outcome of importing a single submission group out of a bulk file.</summary>
/// <param name="Index">Zero-based position of the group within the file (in document order).</param>
/// <param name="Group">The group key (CSV <c>group</c> column) when present; <c>null</c> for JSON groups.</param>
/// <param name="Success">True when the submission was persisted; false when it was skipped or rejected.</param>
/// <param name="Skipped">True when the group was a no-op because the submission already exists (idempotent import); <paramref name="Success"/> is false in this case.</param>
/// <param name="SubmissionId">Id of the created submission when <paramref name="Success"/> is true; otherwise <c>null</c>.</param>
/// <param name="SampleCount">Number of samples the group carried (before any were discarded by the validator).</param>
/// <param name="Errors">Blocking errors that caused the group to be rejected; empty when it succeeded or was skipped.</param>
/// <param name="Warnings">Non-blocking warnings surfaced while importing the group.</param>
public sealed record BulkImportItemResult(
    int Index,
    string? Group,
    bool Success,
    bool Skipped,
    Guid? SubmissionId,
    int SampleCount,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

/// <summary>Summary of a whole bulk import run.</summary>
/// <param name="Total">Total number of submission groups found in the file.</param>
/// <param name="Succeeded">How many groups were persisted.</param>
/// <param name="Skipped">How many groups were skipped because the submission already existed.</param>
/// <param name="Failed">How many groups were rejected (validation/lookup).</param>
/// <param name="Items">Per-group results, in document order.</param>
public sealed record BulkImportResult(
    int Total,
    int Succeeded,
    int Skipped,
    int Failed,
    IReadOnlyList<BulkImportItemResult> Items);

/// <summary>
/// Admin-only bulk import of historical submissions for a single service from a JSON or CSV file.
/// Parsing is strict: a file that can't be parsed (or that yields no submissions) is rejected as a
/// whole with a <see cref="Ingest.Core.Common.ValidationException"/> before anything is written.
/// Once parsing succeeds the import is best-effort and <b>not transactional</b>: each submission
/// group is validated and persisted independently and the returned <see cref="BulkImportResult"/>
/// reports exactly which groups succeeded, were skipped, or failed, so a single bad group never
/// blocks the rest. The import is <b>idempotent</b>: a group whose samples already exist for their
/// reporting window is skipped (not failed), so re-running the same file is safe.
/// </summary>
public interface IBulkImportService
{
    /// <summary>Parse <paramref name="content"/> and import each submission group on behalf of a service.</summary>
    /// <param name="serviceAccountId">The service account every imported submission is attributed to.</param>
    /// <param name="format">Whether <paramref name="content"/> is JSON or CSV.</param>
    /// <param name="content">The raw file text.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A per-group report of what was imported.</returns>
    /// <exception cref="Ingest.Core.Common.ValidationException">The file could not be parsed, or contained no submissions.</exception>
    Task<BulkImportResult> ImportAsync(Guid serviceAccountId, BulkImportFormat format, string content, CancellationToken ct = default);
}
