namespace Ingest.Core.Abstractions;

/// <summary>Summary of a restore: how many documents were written into each collection.</summary>
/// <param name="Restored">Map of collection name → number of documents restored.</param>
public sealed record BackupImportResult(IReadOnlyDictionary<string, int> Restored);

/// <summary>
/// Small, convenience-grade export/import of the entire registry as a single JSON document.
/// <b>Not a substitute for a real database backup</b> — it is intended only for tiny deployments
/// and quick "copy this environment" tasks. A restore <b>replaces</b> the current data wholesale
/// and is not transactional across collections, so it must be used with care (and never against a
/// large or production database — take a proper <c>mongodump</c>/snapshot instead).
/// </summary>
public interface IBackupService
{
    /// <summary>Serialise every collection into one JSON backup document.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The backup as a JSON string (extended JSON, faithful to the stored BSON).</returns>
    Task<string> ExportAsync(CancellationToken ct = default);

    /// <summary>Replace the current data with the contents of a backup produced by <see cref="ExportAsync"/>.</summary>
    /// <param name="json">The backup JSON.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Per-collection counts of restored documents.</returns>
    /// <exception cref="Ingest.Core.Common.ValidationException">The file is empty, not valid JSON, not an Ingest backup, or an unsupported version.</exception>
    Task<BackupImportResult> ImportAsync(string json, CancellationToken ct = default);
}
